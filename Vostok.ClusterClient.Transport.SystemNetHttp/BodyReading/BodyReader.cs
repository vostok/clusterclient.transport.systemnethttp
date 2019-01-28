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
        private readonly Func<long?, bool> useStreaming;
        private readonly long? maxBodySize;
        private readonly ILog log;

        public async Task<BodyReadResult> ReadAsync(HttpResponseMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var contentLength = message.Content.Headers.ContentLength;

                if (contentLength == 0L)
                    return new BodyReadResult(Content.Empty);

                if (contentLength > maxBodySize)
                    return new BodyReadResult(ResponseCode.InsufficientStorage);

                var bodyStream = await message.Content.ReadAsStreamAsync().ConfigureAwait(false);

                if (useStreaming(contentLength))
                    return new BodyReadResult(bodyStream);

                return await (contentLength.HasValue
                    ? ReadWithKnownLengthAsync(bodyStream, contentLength.Value, cancellationToken)
                    : ReadWithUnknownLengthAsync(bodyStream, cancellationToken)).ConfigureAwait(false);
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

                    if (memoryStream.Length + bytesRead > maxBodySize)
                        return new BodyReadResult(ResponseCode.InsufficientStorage);

                    memoryStream.Write(buffer, 0, bytesRead);
                }

                return new BodyReadResult(new Content(memoryStream.GetBuffer(), 0, (int)memoryStream.Length));
            }
        }

        private async Task<BodyReadResult> ReadWithKnownLengthAsync(Stream stream, long length, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private void LogBodyReadFailure(Exception error)
            => log.Error(error, "Failed to read response body.");
    }
}
