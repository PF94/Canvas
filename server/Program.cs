﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace server
{
    public static class Program
    {
        const int CANVAS_WIDTH = 50;
        const int CANVAS_HEIGHT = 50;

        static Bitmap canvas;
        static List<Socket> sockets = new List<Socket>();

        static void Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();
        static async Task MainAsync(string[] args)
        {
            canvas = new Bitmap(CANVAS_WIDTH, CANVAS_HEIGHT);

            var listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listeningSocket.Bind(new IPEndPoint(IPAddress.Any, port: 8080));
            listeningSocket.Listen(100);

            while (true)
            {
                var socket = await listeningSocket.AcceptAsync();
                Task.Run(() => HandleSocketAsync(socket)).ConfigureAwait(false);
            }
        }

        public static byte[] ParsePayloadFromFrame(byte[] incomingFrameBytes)
        {
            var payloadLength = 0L;
            var totalLength = 0L;
            var keyStartIndex = 0L;

            // 125 or less.
            // When it's below 126, second byte is the payload length.
            if ((incomingFrameBytes[1] & 0x7F) < 126)
            {
                payloadLength = incomingFrameBytes[1] & 0x7F;
                keyStartIndex = 2;
                totalLength = payloadLength + 6;
            }

            // 126-65535.
            // When it's 126, the payload length is in the following two bytes
            if ((incomingFrameBytes[1] & 0x7F) == 126)
            {
                payloadLength = BitConverter.ToInt16(new[] { incomingFrameBytes[3], incomingFrameBytes[2] }, 0);
                keyStartIndex = 4;
                totalLength = payloadLength + 8;
            }

            // 65536 +
            // When it's 127, the payload length is in the following 8 bytes.
            if ((incomingFrameBytes[1] & 0x7F) == 127)
            {
                payloadLength = BitConverter.ToInt64(new[] { incomingFrameBytes[9], incomingFrameBytes[8], incomingFrameBytes[7], incomingFrameBytes[6], incomingFrameBytes[5], incomingFrameBytes[4], incomingFrameBytes[3], incomingFrameBytes[2] }, 0);
                keyStartIndex = 10;
                totalLength = payloadLength + 14;
            }

            if (totalLength > incomingFrameBytes.Length)
            {
                throw new Exception("The buffer length is smaller than the data length.");
            }

            var payloadStartIndex = keyStartIndex + 4;

            byte[] key = { incomingFrameBytes[keyStartIndex], incomingFrameBytes[keyStartIndex + 1], incomingFrameBytes[keyStartIndex + 2], incomingFrameBytes[keyStartIndex + 3] };

            var payload = new byte[payloadLength];
            Array.Copy(incomingFrameBytes, payloadStartIndex, payload, 0, payloadLength);
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(payload[i] ^ key[i % 4]);
            }

            return payload;
        }

        public static async Task HandleSocketAsync(Socket socket)
        {
            sockets.Add(socket);

            try
            {
                Console.WriteLine("+");

                var receivedData = new byte[1000000];
                var receivedDataLength = await socket.ReceiveAsync(receivedData, SocketFlags.None);
                var requestString = Encoding.UTF8.GetString(receivedData, 0, receivedDataLength);

                if (new Regex("^GET").IsMatch(requestString))
                {
                    const string eol = "\r\n";

                    var receivedWebSocketKey = new Regex("Sec-WebSocket-Key: (.*)").Match(requestString).Groups[1].Value.Trim();
                    var keyHash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(receivedWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));

                    var response = "HTTP/1.1 101 Switching Protocols" + eol;
                    response += "Connection: Upgrade" + eol;
                    response += "Upgrade: websocket" + eol;
                    response += "Sec-WebSocket-Accept: " + Convert.ToBase64String(keyHash) + eol;
                    response += eol;

                    var responseBytes = Encoding.UTF8.GetBytes(response);

                    socket.Send(responseBytes);
                }

                while (true)
                {
                    receivedData = new byte[1000000];
                    await socket.ReceiveAsync(receivedData, SocketFlags.None);

                    if ((receivedData[0] & (byte)Opcode.CloseConnection) == (byte)Opcode.CloseConnection)
                    {
                        break;
                    }

                    var receivedPayload = ParsePayloadFromFrame(receivedData);
                    var receivedString = Encoding.UTF8.GetString(receivedPayload);

                    Console.WriteLine(">> " + receivedString);

                    await HandleRequestAsync(socket, receivedString.Split(';', StringSplitOptions.None));
                }
            }
            catch (SocketException)
            {
                //throw;
            }
            finally
            {
                Console.WriteLine("-");
                socket.Close();
                sockets.Remove(socket);
            }
        }

        public static async Task HandleRequestAsync(Socket socket, string[] args)
        {
            try
            {
                var command = args[0];

                if (command == "rc")
                {
                    for (int y = 0; y < CANVAS_HEIGHT; y++)
                    {
                        var message = new string[2 + (CANVAS_WIDTH * CANVAS_HEIGHT)];
                        message[0] = "c";
                        message[1] = y.ToString();

                        for (int x = 0; x < CANVAS_WIDTH; x++)
                        {
                            var pixel = canvas.GetPixel(x, y);

                            message[x + 2] = ColorTranslator.ToHtml(pixel).Substring(1);
                        }

                        await socket.SendAsync(message);
                    }

                }
                else if (command == "sp")
                {
                    var x = int.Parse(args[1]);
                    var y = int.Parse(args[2]);
                    var c = ColorTranslator.FromHtml(args[3]);

                    canvas.SetPixel(x, y, c);

                    await AnnounceAsync("pc", x.ToString(), y.ToString(), args[3]);
                }
            }
            catch (SocketException)
            {

            }
            catch (Exception ex)
            {
                if (socket.Connected)
                {
                    await socket.SendAsync("se");
                }
            }

        }

        public static async Task AnnounceAsync(params string[] message)
        {
            foreach (Socket socket in sockets)
            {
                await socket.SendAsync(message);
            }
        }

        public static async Task SendAsync(this Socket socket, params string[] message) => await SendAsync(socket, string.Join(";", message));

        public static async Task SendAsync(this Socket socket, string message)
        {
            Console.WriteLine($"<< {message.Substring(0, Math.Min(message.Length, 50))}");
            await socket.SendAsync(CreateFrameFromString(message), SocketFlags.None);
        }

        public enum Opcode
        {
            Fragment = 0,
            Text = 1,
            Binary = 2,
            CloseConnection = 8,
            Ping = 9,
            Pong = 10
        }

        public static byte[] CreateFrameFromString(string message, Opcode opcode = Opcode.Text)
        {
            var payload = Encoding.UTF8.GetBytes(message);

            byte[] frame;

            if (payload.Length < 126)
            {
                frame = new byte[1 /*op code*/ + 1 /*payload length*/ + payload.Length /*payload bytes*/];
                frame[1] = (byte)payload.Length;
                Array.Copy(payload, 0, frame, 2, payload.Length);
            }
            else if (payload.Length >= 126 && payload.Length <= 65535)
            {
                frame = new byte[1 /*op code*/ + 1 /*payload length option*/ + 2 /*payload length*/ + payload.Length /*payload bytes*/];
                frame[1] = 126;
                frame[2] = (byte)((payload.Length >> 8) & 255);
                frame[3] = (byte)(payload.Length & 255);
                Array.Copy(payload, 0, frame, 4, payload.Length);
            }
            else
            {
                frame = new byte[1 /*op code*/ + 1 /*payload length option*/ + 8 /*payload length*/ + payload.Length /*payload bytes*/];
                frame[1] = 127; // <-- Indicates that payload length is in following 8 bytes.
                frame[2] = (byte)((payload.Length >> 56) & 255);
                frame[3] = (byte)((payload.Length >> 48) & 255);
                frame[4] = (byte)((payload.Length >> 40) & 255);
                frame[5] = (byte)((payload.Length >> 32) & 255);
                frame[6] = (byte)((payload.Length >> 24) & 255);
                frame[7] = (byte)((payload.Length >> 16) & 255);
                frame[8] = (byte)((payload.Length >> 8) & 255);
                frame[9] = (byte)(payload.Length & 255);
                Array.Copy(payload, 0, frame, 10, payload.Length);
            }

            frame[0] = (byte)((byte)opcode | 0x80 /*FIN bit*/);

            return frame;
        }
    }
}