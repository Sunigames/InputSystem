using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.LowLevel
{
    /// <summary>
    /// A specialized event that contains the current IME Composition string, if IME is enabled and active.
    /// This event contains the entire current string to date, and once a new composition is submitted will send a blank string event.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = InputEvent.kBaseEventSize + sizeof(int) + (sizeof(char)))]
    public struct IMECompositionEvent : IInputEventTypeInfo
    {
        public const int Type = 0x494D4543;

        [FieldOffset(0)]
        internal InputEvent baseEvent;

        [FieldOffset(InputEvent.kBaseEventSize)]
        internal int length;

        [FieldOffset(InputEvent.kBaseEventSize + sizeof(int))]
        internal char bufferStart;

        public FourCC typeStatic => Type;

        internal IMECompositionString GetComposition()
        {
            unsafe
            {
                fixed(char* buffer = &bufferStart)
                {
                    return new IMECompositionString(new IntPtr(buffer), length);
                }
            }
        }

        /// <summary>
        /// Queues up an IME Composition Event.  IME Event sizes are variable, and this simplifies the process of aligning up the Input Event information and actual IME composition string.
        /// </summary>
        /// <param name="deviceId">ID of the device (see <see cref="InputDevice.deviceId") to which the composition event should be sent to. Should be an <see cref="ITextInputReceiver"/> device. Will trigger <see cref="ITextInputReceiver.OnIMECompositionChanged"/> call when processed.</param>
        /// <param name="str">The IME characters to be sent. This can be any length, or left blank to represent a resetting of the IME dialog.</param>
        /// <param name="time">The time in seconds, the event was generated at.  This uses the same timeline as <see cref="Time.realtimeSinceStartup"/></param>
        public static void QueueEvent(int deviceId, string str, double time)
        {
            unsafe
            {
                int sizeInBytes = (InputEvent.kBaseEventSize + sizeof(int) + sizeof(char)) + (sizeof(char) * str.Length);
                NativeArray<Byte> eventBuffer = new NativeArray<byte>(sizeInBytes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                
                byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(eventBuffer);
                InputEvent* evt = (InputEvent*)ptr;

                *evt = new InputEvent(Type, sizeInBytes, deviceId, time);
                ptr += InputEvent.kBaseEventSize;

                int* lengthPtr = (int*)ptr;
                *lengthPtr = str.Length;

                ptr += sizeof(int);
                
                fixed(char* p = str)
                {
                    UnsafeUtility.MemCpy(ptr, p, str.Length * sizeof(char));
                }

                InputSystem.QueueEvent(new InputEventPtr(evt));
            }
        }
    }

    /// <summary>
    /// A struct representing an string of characters generated by an IME for text input.
    /// </summary>
    /// <remarks>
    /// This is the internal representation of character strings in the event stream. It is exposed to user content through the
    /// <see cref="ITextInputReceiver.OnIMECompositionChanged"/> method. It can easily be converted to a normal C# string using
    ///  <see cref="ToString"/>, but is exposed as the raw struct to avoid allocating memory by default.
    /// 
    /// Because this string does not allocate, it is only valid, and can only be read within the <see cref="ITextInputReceiver.OnIMECompositionChanged"/> that it is recieved, and will otherwise return an invalid composition.
    /// </remarks>
    public unsafe struct IMECompositionString : IEnumerable<char>
    {
        internal struct Enumerator : IEnumerator<char>
        {
            IntPtr m_BufferStart;
            int m_CharacterCount;
            char m_CurrentCharacter;
            int m_CurrentIndex;

            public Enumerator(IMECompositionString compositionString)
            {
                m_BufferStart = compositionString.m_CharBuffer;
                m_CharacterCount = compositionString.length;
                m_CurrentCharacter = '\0';
                m_CurrentIndex = -1;
            }

            public bool MoveNext()
            {
                m_CurrentIndex++;

                if (m_CurrentIndex == m_CharacterCount)
                    return false;

                char* ptr = (char*)m_BufferStart.ToPointer();
                m_CurrentCharacter = *(ptr + m_CurrentIndex);

                return true;
            }

            public void Reset()
            {
                m_CurrentIndex = -1;
            }

            public void Dispose()
            {
            }

            public char Current => m_CurrentCharacter;

            object IEnumerator.Current => Current;
        }

        int m_Length;
        /// <summary>
        /// This returns the total number of characters in the IME composition.
        /// </summary>
        /// <remarks>
        /// If 0, this event represents a clearing of the current IME dialog.
        /// </remarks>
        public int length { get { return m_Length; }}

        IntPtr m_CharBuffer;


        /// <summary>
        /// An Indexer into an individual character in the IME Composition. Will throw an out of range exception if the index is greater than the length of the composition.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The requested index is greater than the <cref="IMECompositionString.length"> of the composition.</exception>
        /// <value>Returns the character at the requested index in UTF-32 encoding.</value>
        public char this[int index]
        {
            get
            {
                if (index >= m_Length || index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                char* ptr = (char*)m_CharBuffer.ToPointer();
                return *(ptr + index);
            }
        }


        internal IMECompositionString(IntPtr charBuffer, int length)
        {
            m_Length = length;
            m_CharBuffer = charBuffer;
        }

        /// <summary>
        /// Converts the IMEComposition to a new string.
        /// </summary>
        /// <remarks>
        /// This will generate garbage by allocating a new string.
        /// </remarks>
        /// <returns>The IME Composition as a string</returns>
        public override string ToString()
        {
            char* ptr = (char*)m_CharBuffer.ToPointer();
            return new string(ptr, 0, m_Length);
        }

        public IEnumerator<char> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
