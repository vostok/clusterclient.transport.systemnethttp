using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Transport.SystemNetHttp.Helpers;
using Vostok.Logging.Abstractions;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.ResponseReading
{
    internal class BodyReader : IBodyReader
    {
        private readonly Func<long, byte[]> bufferFactory;
        private readonly Func<long?, bool> useStreaming;
        private readonly long? maxBodySize;
        private readonly ILog log;

        public BodyReader(
            Func<long, byte[]> bufferFactory,
            Func<long?, bool> useStreaming, 
            long? maxBodySize, 
            ILog log)
        {
            this.bufferFactory = bufferFactory;
            this.useStreaming = useStreaming;
            this.maxBodySize = maxBodySize;
            this.log = log;
        }

        public async Task<BodyReadResult> ReadAsync(HttpResponseMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var contentLength = message.Content.Headers.ContentLength;
                if (contentLength.HasValue)
                {
                    if (contentLength.Value == 0L)
                        return new BodyReadResult(Content.Empty);

                    if (contentLength.Value > maxBodySize || contentLength.Value > int.MaxValue)
                    {
                        LogBodyTooLarge(Math.Min(int.MaxValue, maxBodySize ?? long.MaxValue), contentLength.Value);
                        return new BodyReadResult(ResponseCode.InsufficientStorage);
                    }
                }

                var bodyStream = await message.Content.ReadAsStreamAsync().ConfigureAwait(false);

                if (useStreaming(contentLength))
                    return new BodyReadResult(bodyStream);

                using (bodyStream)
                {
                    return await (contentLength.HasValue
                        ? ReadWithKnownLengthAsync(bodyStream, (int) contentLength.Value, cancellationToken)
                        : ReadWithUnknownLengthAsync(bodyStream, cancellationToken)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                LogBodyReadFailure(error);

                return new BodyReadResult(ResponseCode.ReceiveFailure);
            }
        }

        private async Task<BodyReadResult> ReadWithUnknownLengthAsync(Stream stream, CancellationToken cancellationToken)
        {
            using (BufferPool.Acquire(out var buffer))
            {
                var memoryStream = new MemoryStream();

                while (true)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;

                    if (maxBodySize.HasValue && memoryStream.Length + bytesRead > maxBodySize)
                    {
                        LogBodyTooLarge(maxBodySize.Value, null);
                        return new BodyReadResult(ResponseCode.InsufficientStorage);
                    }

                    memoryStream.Write(buffer, 0, bytesRead);
                }

                return new BodyReadResult(new Content(memoryStream.GetBuffer(), 0, (int)memoryStream.Length));
            }
        }

        private async Task<BodyReadResult> ReadWithKnownLengthAsync(Stream stream, int contentLength, CancellationToken cancellationToken)
        {
            var array = bufferFactory(contentLength);

            var totalBytesRead = 0;

            if (contentLength < Constants.LOHObjectSizeThreshold)
            {
                while (totalBytesRead < contentLength)
                {
                    var bytesToRead = Math.Min(contentLength - totalBytesRead, BufferPool.BufferSize);
                    var bytesRead = await stream.ReadAsync(array, totalBytesRead, bytesToRead, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                        break;

                    totalBytesRead += bytesRead;
                }
            }
            else
            {
                using (BufferPool.Acquire(out var buffer))
                {
                    while (totalBytesRead < contentLength)
                    {
                        var bytesToRead = Math.Min(contentLength - totalBytesRead, buffer.Length);
                        var bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead, cancellationToken).ConfigureAwait(false);
                        if (bytesRead == 0)
                            break;

                        Buffer.BlockCopy(buffer, 0, array, totalBytesRead, bytesRead);

                        totalBytesRead += bytesRead;
                    }
                }
            }

            if (totalBytesRead < contentLength)
                throw new EndOfStreamException($"Response stream ended prematurely. Read only {totalBytesRead} byte(s), but Content-Length header specified {contentLength}.");

            return new BodyReadResult(new Content(array, 0, contentLength));
        }

        private void LogBodyReadFailure(Exception error)
            => log.Error(error, "Failed to read response body.");

        private void LogBodyTooLarge(long limit, long? actual)
            => log.Error("Response body is too large. Limit = {Limit}. Actual size = {Size}.", limit, actual?.ToString() ?? "?");
    }
}
