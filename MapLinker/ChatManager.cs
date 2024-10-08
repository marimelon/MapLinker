﻿using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.GeneratedSheets;

namespace MapLinker
{
    /// <summary>
    /// Manager that handles displaying output in the chat box.
    /// </summary>
    public class ChatManager : IDisposable
    {
        private readonly Channel<string> chatBoxMessages = Channel.CreateUnbounded<string>();

        [Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B FA 48 8B D9 45 84 C9")]
        private readonly ProcessChatBoxDelegate processChatBox = null!;

        private IFramework framework { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChatManager"/> class.
        /// </summary>
        public ChatManager(IFramework framework, IGameInteropProvider GameInteropProvider)
        {
            GameInteropProvider.InitializeFromAttributes(this);
            this.framework = framework;
            this.framework.Update += this.FrameworkUpdate;
        }

        private unsafe delegate void ProcessChatBoxDelegate(UIModule* uiModule, IntPtr message, IntPtr unused, byte a4);

        /// <inheritdoc/>
        public void Dispose()
        {
            this.framework.Update -= this.FrameworkUpdate;

            this.chatBoxMessages.Writer.Complete();
        }

        /// <summary>
        /// Process a command through the chat box.
        /// </summary>
        /// <param name="message">Message to send.</param>
        public async void SendMessage(string message)
        {
            await this.chatBoxMessages.Writer.WriteAsync(message);
        }

        /// <summary>
        /// Clear the queue of messages to send to the chatbox.
        /// </summary>
        public void Clear()
        {
            var reader = this.chatBoxMessages.Reader;
            while (reader.Count > 0 && reader.TryRead(out var _))
                continue;
        }

        private void FrameworkUpdate(IFramework framework)
        {
            if (this.chatBoxMessages.Reader.TryRead(out var message))
            {
                this.SendMessageInternal(message);
            }
        }

        private unsafe void SendMessageInternal(string message)
        {
            var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
            var uiModule = framework->GetUIModule();

            using var payload = new ChatPayload(message);
            var payloadPtr = Marshal.AllocHGlobal(400);
            Marshal.StructureToPtr(payload, payloadPtr, false);

            this.processChatBox(uiModule, payloadPtr, IntPtr.Zero, 0);

            Marshal.FreeHGlobal(payloadPtr);
        }

        [StructLayout(LayoutKind.Explicit)]
        private readonly struct ChatPayload : IDisposable
        {
            [FieldOffset(0)]
            private readonly IntPtr textPtr;

            [FieldOffset(16)]
            private readonly ulong textLen;

            [FieldOffset(8)]
            private readonly ulong unk1;

            [FieldOffset(24)]
            private readonly ulong unk2;

            internal ChatPayload(string text)
            {
                var stringBytes = Encoding.UTF8.GetBytes(text);
                this.textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);

                Marshal.Copy(stringBytes, 0, this.textPtr, stringBytes.Length);
                Marshal.WriteByte(this.textPtr + stringBytes.Length, 0);

                this.textLen = (ulong)(stringBytes.Length + 1);

                this.unk1 = 64;
                this.unk2 = 0;
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(this.textPtr);
            }
        }
    }
}

