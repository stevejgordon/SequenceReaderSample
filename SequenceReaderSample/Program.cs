using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

namespace SequenceReaderSample
{
    internal class Program
    {
        private const int MaxStackLength = 128;
        private const int ItemCount = 10;
        private const byte Comma = (byte)',';

        private static async Task Main()
        {
            var stream = CreateStreamOfItems();

            var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 32)); // forcing a small buffer for this sample

            while (true)
            {
                var result = await pipeReader.ReadAsync(); // read from the pipe

                var buffer = result.Buffer;
      
                var position = ReadItems(buffer, result.IsCompleted); // read complete items from the current buffer

                if (result.IsCompleted) 
                    break; // exit if we've read everything from the pipe

                pipeReader.AdvanceTo(position, buffer.End); //advance our position in the pipe
            }

            pipeReader.Complete(); // mark the PipeReader as complete

            Console.ReadKey();
        }

        /// <summary>
        /// Setup a stream. For this sample we can imagine came from a HttpResponseMessage.
        /// </summary>
        private static Stream CreateStreamOfItems()
        {
            var stream = new MemoryStream();

            using var writer = new StreamWriter(stream, new UTF8Encoding(), leaveOpen: true);

            for (var i = 1; i < ItemCount; i++)
            {
                writer.Write($"Item {i},");
                writer.Flush();
            }

            writer.Write($"Item {ItemCount}");
            writer.Flush();

            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        private static SequencePosition ReadItems(in ReadOnlySequence<byte> sequence, bool isCompleted)
        {
            var reader = new SequenceReader<byte>(sequence);

            while (!reader.End) // loop until we've read the entire sequence
            {
                if (reader.TryReadTo(out ReadOnlySpan<byte> itemBytes, Comma, advancePastDelimiter: true)) // we have an item to handle
                {
                    var stringLine = Encoding.UTF8.GetString(itemBytes);
                    Console.WriteLine(stringLine);
                }
                else if (isCompleted) // read last item which has no final delimiter
                {
                    var stringLine = ReadLastItem(sequence.Slice(reader.Position));
                    Console.WriteLine(stringLine);
                    reader.Advance(sequence.Length); // advance reader to the end
                }
                else // no more items in this sequence
                {
                    break;
                }
            }

            return reader.Position;
        }

        private static string ReadLastItem(in ReadOnlySequence<byte> sequence)
        {
            var length = (int)sequence.Length;

            string stringLine;

            if (length < MaxStackLength) // if the item is small enough we'll stack allocate the buffer
            {
                Span<byte> byteBuffer = stackalloc byte[length];
                sequence.CopyTo(byteBuffer);
                stringLine = Encoding.UTF8.GetString(byteBuffer);
            }
            else // otherwise we'll rent an array to use as the buffer
            {
                var byteBuffer = ArrayPool<byte>.Shared.Rent(length);

                try
                {
                    sequence.CopyTo(byteBuffer);
                    stringLine = Encoding.UTF8.GetString(byteBuffer.AsSpan().Slice(0, length));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(byteBuffer);
                }
            }

            return stringLine;
        }
    }
}
