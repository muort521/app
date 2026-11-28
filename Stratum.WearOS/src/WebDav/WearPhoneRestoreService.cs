// Copyright (C) 2026 Stratum fork contributors
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content;
using Android.Gms.Wearable;
using Stratum.Droid.Shared.Wear;

namespace Stratum.WearOS.WebDav
{
    /// <summary>
    /// Sends a decrypted backup to the paired phone over the Wearable Data Layer.
    /// The phone performs the actual database restore so the result is durable.
    /// If no phone is reachable, the watch keeps its local cache restore.
    /// </summary>
    public static class WearPhoneRestoreService
    {
        private const string ProtocolVersion = "protocol_v4.0";
        private const string RestorePath = "restore_backup";

        public static async Task SendAsync(Context context, WearRestoreRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null || request.Backup == null) throw new ArgumentException("Restore request is empty");

            var capabilityInfo = await WearableClass.GetCapabilityClient(context)
                .GetCapabilityAsync(ProtocolVersion, CapabilityClient.FilterReachable);

            var node = capabilityInfo?.Nodes?.MaxBy(n => n.IsNearby);
            Android.Util.Log.Info("STRATUMWEAR", $"Restore nodes={capabilityInfo?.Nodes?.Count ?? 0}, node={node?.Id}, nearby={node?.IsNearby}");
            if (node == null)
            {
                throw new InvalidOperationException("No paired phone is reachable");
            }

            var client = WearableClass.GetChannelClient(context);
            ChannelClient.IChannel channel = null;

            try
            {
                Android.Util.Log.Info("STRATUMWEAR", $"Opening restore channel to {node.Id}");
                channel = await client.OpenChannelAsync(node.Id, RestorePath);
                Android.Util.Log.Info("STRATUMWEAR", $"Restore channel opened: {channel?.Path}");
                var payload = JsonSerializer.SerializeToUtf8Bytes(request);
                var stream = await client.GetOutputStreamAsync(channel);

                try
                {
                    await stream.WriteAsync(payload);
                }
                finally
                {
                    stream.Close();
                }

                // Give the phone service time to consume the channel before closing it.
                await Task.Delay(700);
            }
            finally
            {
                if (channel != null)
                {
                    await client.CloseAsync(channel);
                }
            }
        }
    }
}
