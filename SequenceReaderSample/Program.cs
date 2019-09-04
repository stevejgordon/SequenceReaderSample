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
        private const int ItemCount = 10;
        private const byte Comma = (byte)',';

        private static async Task Main()
        {
            var stream = CreateStreamOfItems();

            var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 64)); // forcing a small buffer

            while (true)
            {
                var result = await pipeReader.ReadAsync();

                var buffer = result.Buffer;

                if (result.IsCompleted && buffer.Length > 0)
                {
                    ReadLastItem(buffer);
                    break;
                }

                var position = ReadItem(buffer);
                pipeReader.AdvanceTo(position, buffer.End);
            }

            pipeReader.Complete();

            Console.ReadKey();
        }

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

        private static SequencePosition ReadItem(in ReadOnlySequence<byte> sequence)
        {
            var reader = new SequenceReader<byte>(sequence);

            while (!reader.End)
            {
                if (reader.TryReadTo(out ReadOnlySpan<byte> itemBytes, Comma, true))
                {
                    var stringLine = Encoding.UTF8.GetString(itemBytes);
                    Console.WriteLine(stringLine);
                }
                else // no more items in this sequence
                {
                    break;
                }
            }

            return reader.Position;
        }

        private static void ReadLastItem(in ReadOnlySequence<byte> sequence)
        {
            var length = (int)sequence.Length;

            var reader = new SequenceReader<byte>(sequence);

            var bytes = ArrayPool<byte>.Shared.Rent(length);

            var byteSpan = bytes.AsSpan(0, length);

            try
            {
                reader.TryCopyTo(byteSpan);
                var stringLine = Encoding.UTF8.GetString(byteSpan);
                Console.WriteLine(stringLine);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
    }
}
