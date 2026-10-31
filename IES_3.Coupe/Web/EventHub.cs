using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IES_2.Coupe.Web
{
    /// <summary>
    /// Fans server-side events out to every connected browser over Server-Sent Events. Each client
    /// gets its own bounded queue that drops the oldest items when a phone falls behind (screen off,
    /// weak Wi-Fi), so a slow client can never stall the diagnostics loop that publishes into it.
    /// </summary>
    public sealed class EventHub
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        private readonly ConcurrentDictionary<Guid, Channel<SseItem<string>>> _clients = new();

        /// <summary>Raised with the new number of open browser streams whenever one opens or closes.</summary>
        public event Action<int>? ClientCountChanged;

        public void Publish(string eventType, object payload)
        {
            if (_clients.IsEmpty) return;
            var item = new SseItem<string>(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), eventType);
            foreach (var channel in _clients.Values)
                channel.Writer.TryWrite(item);
        }

        /// <summary>One browser's event stream: the current state first, then everything published after it.</summary>
        public async IAsyncEnumerable<SseItem<string>> Stream(Func<object> initialState, [EnumeratorCancellation] CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateBounded<SseItem<string>>(new BoundedChannelOptions(512)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            _clients[id] = channel;
            ClientCountChanged?.Invoke(_clients.Count);
            try
            {
                // Subscribed before taking the snapshot, so nothing published in between is lost.
                yield return new SseItem<string>(JsonSerializer.Serialize(initialState(), JsonSerializerOptions.Web), "state");

                Task<bool>? wait = null;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    wait ??= channel.Reader.WaitToReadAsync(ct).AsTask();
                    var done = await Task.WhenAny(wait, Task.Delay(HeartbeatInterval, ct));
                    if (done != wait)
                    {
                        // Keeps idle connections from being dropped by phones' power saving.
                        yield return new SseItem<string>("{}", "ping");
                        continue;
                    }
                    if (!await wait) yield break;
                    wait = null;
                    while (channel.Reader.TryRead(out var item))
                        yield return item;
                }
            }
            finally
            {
                _clients.TryRemove(id, out _);
                ClientCountChanged?.Invoke(_clients.Count);
            }
        }
    }
}
