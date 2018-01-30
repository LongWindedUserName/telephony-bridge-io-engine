using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionEngine
{
    class MessageStream
    {
        private readonly Queue<byte[]> _toEmitQueue = new Queue<byte[]>();
        private byte[] _leftoverBytes;
        private int _messageStartIndex = -1;

        public void AddBytes(byte[] bytes, int length)
        {
            var toRead = new byte[length];
            Array.Copy(bytes, toRead, length);

            for(var i = 0; i < length; i++)
            {
                if (toRead[i] == 2) // Found start text
                {
                    if (_messageStartIndex != -1) throw new Exception("Invalid format - In a message");
                    _messageStartIndex = i + 1; // Don't include STX
                }
                if (toRead[i] == 3) // Found end text
                {
                    if (_messageStartIndex == -1) throw new Exception("Invalid format - Must be in a message");

                    byte[] msgArray;
                    if (_leftoverBytes != null)
                    {
                        // Need to handle the split message from last byte[]
                        var msgLength = (_leftoverBytes.Length - _messageStartIndex) + i;
                        msgArray = new byte[msgLength];

                        Array.Copy(_leftoverBytes, _messageStartIndex, msgArray, 0, _leftoverBytes.Length - _messageStartIndex);
                        Array.Copy(toRead, 0, msgArray, _leftoverBytes.Length - _messageStartIndex, i);

                        _leftoverBytes = null;
                    }
                    else
                    {
                        var msgLength = i - _messageStartIndex;
                        msgArray = new byte[msgLength];

                        Array.Copy(toRead, _messageStartIndex, msgArray, 0, msgLength);
                    }

                    _messageStartIndex = -1;
                    _toEmitQueue.Enqueue(msgArray);
                }
            }

            if (_messageStartIndex != -1) // have a split message, save until next AddBytes call
                _leftoverBytes = toRead;
        }

        public byte[] Read()
        {
            return _toEmitQueue.Any() ? _toEmitQueue.Dequeue() : null;
        }

        public IEnumerable<byte[]> ReadAll()
        {
            while (_toEmitQueue.Any())
                yield return _toEmitQueue.Dequeue();
        }
    }
}
