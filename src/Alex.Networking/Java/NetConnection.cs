﻿#region Imports

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Alex.Networking.Java.Events;
using Alex.Networking.Java.Packets;
using Alex.Networking.Java.Packets.Play;
using Alex.Networking.Java.Util;
using Ionic.Zlib;
using Microsoft.IO;
using MiNET.Utils;
using NLog;

#endregion

namespace Alex.Networking.Java
{
    public delegate void ConnectionConfirmed(NetConnection conn);
    public class NetConnection : IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(NetConnection));
        
        private CancellationTokenSource CancellationToken { get; }
        protected ConnectionConfirmed ConnectionConfirmed { get; }
        private Direction Direction { get; }
        private Socket Socket { get; }

		private DedicatedThreadPool ThreadPool { get; }

		public NetConnection(Direction direction, Socket socket, ConnectionConfirmed confirmdAction = null, DedicatedThreadPool threadPool = null)
        {
            Direction = direction;
            Socket = socket;
            RemoteEndPoint = Socket.RemoteEndPoint;
	        ThreadPool = threadPool;

            ConnectionConfirmed = confirmdAction;

            CancellationToken = new CancellationTokenSource();

			ConnectionState = ConnectionState.Handshake;
	        IsConnected = true;

			PacketWriteQueue = new BlockingCollection<EnqueuedPacket>();
			//HandlePacketQueue = new BlockingCollection<TemporaryPacketData>();
        }

        public EventHandler<PacketReceivedEventArgs> OnPacketReceived;
        public EventHandler<ConnectionClosedEventArgs> OnConnectionClosed;

        public EndPoint RemoteEndPoint { get; private set; }
		public ConnectionState ConnectionState { get; set; }
		public bool CompressionEnabled { get; set; }
		public int CompressionThreshold = 256;

	    public bool EncryptionInitiated { get; private set; } = false;
		protected byte[] SharedSecret { get; private set; }

		public bool IsConnected { get; private set; }

		private BlockingCollection<EnqueuedPacket> PacketWriteQueue { get; }
		public bool LogExceptions { get; set; } = true;

	    private Thread NetworkProcessing { get; set; }
		private Thread NetworkWriting { get; set; }
		public void Initialize()
        {
	        Socket.Blocking = true;

		   	NetworkProcessing = new Thread(ProcessNetwork)
            {
				IsBackground = true
            };
            NetworkProcessing.Start();

			NetworkWriting = new Thread(SendQueue)
			{
				IsBackground = true
			};
			NetworkWriting.Start();
        }

        public void Stop()
        {
            if (CancellationToken.IsCancellationRequested) return;
            CancellationToken.Cancel();

            if (SocketConnected(Socket))
            {
                //TODO
                Disconnected(true);
            }
            else
            {
                Disconnected(false);
            }
        }

        private object _disconnectSync = false;

        private void Disconnected(bool notified)
        {
            lock (_disconnectSync)
            {
                if ((bool) _disconnectSync) return;
                _disconnectSync = true;
            }

            if (!CancellationToken.IsCancellationRequested)
            {
                CancellationToken.Cancel();
            }

            Socket.Shutdown(SocketShutdown.Both);
            Socket.Close();

            OnConnectionClosed?.Invoke(this, new ConnectionClosedEventArgs(this, notified));

	        IsConnected = false;
        }

	    public void InitEncryption(byte[] sharedKey)
	    {
		    SharedSecret = sharedKey;
			_readerStream.InitEncryption(SharedSecret, false);
			_sendStream.InitEncryption(SharedSecret, true);
		    EncryptionInitiated = true;
	    }

	    public static RecyclableMemoryStreamManager StreamManager { get; }= new RecyclableMemoryStreamManager();
	    private MinecraftStream _readerStream;

	    private void ProcessNetwork()
	    {
		    int lastPacketId = 0;
		    try
		    {
			    using (NetworkStream ns = new NetworkStream(Socket))
			    {
				    using (MinecraftStream ms = new MinecraftStream(ns))
				    {
					    _readerStream = ms;

					    while (!CancellationToken.IsCancellationRequested)
					    {
						    Packets.Packet packet = null;
						    int packetId;
						    byte[] packetData;

						    if (!CompressionEnabled)
						    {
							    int length = ms.ReadVarInt();

							    int packetIdLength;
							    packetId = ms.ReadVarInt(out packetIdLength);
							    lastPacketId = packetId;
							    if (length - packetIdLength > 0)
							    {
								    packetData = ms.Read(length - packetIdLength);
							    }
							    else
							    {
								    packetData = new byte[0];
							    }
						    }
						    else
						    {
							    int packetLength = ms.ReadVarInt();

							    int br;
							    int dataLength = ms.ReadVarInt(out br);

							    int readMore;
							    if (dataLength == 0)
							    {
								    packetId = ms.ReadVarInt(out readMore);
								    lastPacketId = packetId;
								    packetData = ms.Read(packetLength - (br + readMore));
							    }
							    else
							    {
								    byte[] data = ms.Read(packetLength - br);
								    using (MinecraftStream a = new MinecraftStream())
								    {
									    using (ZlibStream outZStream = new ZlibStream(a,
										    CompressionMode.Decompress, CompressionLevel.Default, true))
									    {
										    outZStream.Write(data, 0, data.Length);
									    }

									    a.Seek(0, SeekOrigin.Begin);

									    int l;
									    packetId = a.ReadVarInt(out l);
									    lastPacketId = packetId;
									    packetData = a.Read(dataLength - l);
								    }
							    }
						    }

						    packet = MCPacketFactory.GetPacket(Direction, ConnectionState, packetId);
						    if (packet == null)
						    {
							    if (UnhandledPacketsFilter[ConnectionState]
								    .TryAdd(packetId, 1))
							    {
								    Log.Debug(
									    $"Unhandled packet in {ConnectionState}! 0x{packetId.ToString("x2")} = {(ConnectionState == ConnectionState.Play ? MCPacketFactory.GetPlayPacketName(packetId) : "Unknown")}");
							    }
							    else
							    {
								    UnhandledPacketsFilter[ConnectionState][packetId] =
									    UnhandledPacketsFilter[ConnectionState][packetId] + 1;
							    }

							    continue;
						    }

						    if (ConnectionState == ConnectionState.Play)
						    {
							    if (ShouldAddToProcessing(packet))
							    {
								    Interlocked.Increment(ref _queued);
								    ThreadPool.QueueUserWorkItem(() =>
								    {
									    ProcessPacket(packet, packetData);
									    Interlocked.Decrement(ref _queued);
								    });
							    }
						    }
						    else
						    {
							    ProcessPacket(packet, packetData);
						    }
					    }
				    }
			    }
		    }
		    catch (Exception ex)
		    {
			    if (ex is OperationCanceledException) return;
			    if (ex is EndOfStreamException) return;
			    if (ex is IOException) return;

			    if (LogExceptions)
				    Log.Warn(
					    $"Failed to process network (Last packet: 0x{lastPacketId:X2} State: {ConnectionState}): " +
					    ex);
		    }
		    finally
		    {
			    Disconnected(false);
		    }
	    }

	    protected virtual bool ShouldAddToProcessing(Packet packet)
	    {
		    return true;
	    }
	    
	    private long _queued = 0;

	    private void ProcessPacket(Packet packet, byte[] data)
		{
			//	if (packet.Log)
				packet.Stopwatch.Start();
			
			using (var memoryStream = StreamManager.GetStream("Packet Stream {0}", data, 0, data.Length))
			{
				using (MinecraftStream minecraftStream = new MinecraftStream(memoryStream))
				{
					packet.Decode(minecraftStream);
				}
			}
			
			HandlePacket(packet);
			
			packet.Stopwatch.Stop();
			if (packet.Stopwatch.ElapsedMilliseconds > 250)
			{
				Log.Warn($"Packet handling took too long: {packet.GetType()} | {packet.Stopwatch.ElapsedMilliseconds}ms Processed bytes: {data.Length} (Queue size: {_queued})");
			}
		}


	    private Dictionary<ConnectionState, ConcurrentDictionary<int, int>> UnhandledPacketsFilter =
		    new Dictionary<ConnectionState, ConcurrentDictionary<int, int>>()
		    {
			    {ConnectionState.Handshake, new ConcurrentDictionary<int, int>() },
			    {ConnectionState.Status, new ConcurrentDictionary<int, int>() },
			    {ConnectionState.Login, new ConcurrentDictionary<int, int>() },
			    {ConnectionState.Play, new ConcurrentDictionary<int, int>() },
			};


		protected virtual void HandlePacket(Packets.Packet packet)
	    {
			PacketReceivedEventArgs args = new PacketReceivedEventArgs(packet);
		    OnPacketReceived?.Invoke(this, args);
	    }

        private void PacketReceivedCallback(IAsyncResult ar)
        {
            OnPacketReceived.EndInvoke(ar);
            PacketReceivedEventArgs args = (PacketReceivedEventArgs)ar.AsyncState;
            if (args.IsInvalid)
            {
                Log.Warn("Packet reported as invalid!");
            }
        }

        private void SendDataInternal(byte[] buffer)
        {
	        if (CancellationToken.IsCancellationRequested) return;
            SendData sendData = new SendData(buffer);
            Socket.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, SendCallback, sendData);
        }

        private void SendCallback(IAsyncResult ar)
        {
	        try
	        {
		        SocketError result;
		        int sent = Socket.EndSend(ar, out result);

		        SendData data = (SendData) ar.AsyncState;

		        if (result == SocketError.Success)
		        {
			        if (sent != data.Buffer.Length)
			        {
				        Log.Warn("Sent {0} out of {1} bytes!", sent, data.Buffer.Length);
			        }
		        }
		        else
		        {
			        Log.Warn("Failed to send data! (Reason: {0})", result);
		        }
			}
			catch { }
        }

	    public void SendPacket(Packet packet)
	    {
			if (packet.PacketId == -1) throw new Exception();

			//if (packet.Log)
			//	Log.Info($"Sending packet ({CompressionEnabled}:{EncryptionInitiated}): {packet} 0x{packet.PacketId:X2}");
//if (packet.PacketId == 14) Log.Debug($"PACKET = {packet.ToString()}");
			PacketWriteQueue.Add(new EnqueuedPacket(packet, EncryptionInitiated, CompressionEnabled));
	    }

	    private MinecraftStream _sendStream;
	    private void SendQueue()
	    {
		    using (NetworkStream ms = new NetworkStream(Socket))
		    {
			    using (MinecraftStream mc = new MinecraftStream(ms))
			    {
				    _sendStream = mc;
				    while (!CancellationToken.IsCancellationRequested)
				    {
					    try
					    {
						    EnqueuedPacket packet = PacketWriteQueue.Take(CancellationToken.Token);
						    var data = EncodePacket(packet);

							mc.WriteVarInt(data.Length);
							mc.Write(data);
						}
						catch (EndOfStreamException) { }
					    catch (OperationCanceledException)
					    {
						    break;
					    }
				    }
			    }
		    }
	    }

	    private byte[] EncodePacket(EnqueuedPacket enqueued)
	    {
		    var packet = enqueued.Packet;
		    byte[] encodedPacket;
		    using (MemoryStream ms = new MemoryStream())
		    {
			    using (MinecraftStream mc = new MinecraftStream(ms))
			    {
				    mc.WriteVarInt(packet.PacketId);
				    packet.Encode(mc);

				    encodedPacket = ms.ToArray();

				    mc.Position = 0;
				    mc.SetLength(0);

				    if (enqueued.CompressionEnabled)
				    {
					    if (encodedPacket.Length >= CompressionThreshold)
					    {
						    //byte[] compressed;
						    //CompressData(encodedPacket, out compressed);

						    mc.WriteVarInt(encodedPacket.Length);
						    using (ZlibStream outZStream = new ZlibStream(mc, CompressionMode.Compress, CompressionLevel.Default, true))
						    {
							    outZStream.Write(encodedPacket, 0, encodedPacket.Length);
						    }
						   // mc.Write(compressed);
					    }
					    else //Uncompressed
					    {
						    mc.WriteVarInt(0);
						    mc.Write(encodedPacket);
					    }

					    encodedPacket = ms.ToArray();
				    }
			    }
		    }

		    return encodedPacket;
	    }

	    private bool SocketConnected(Socket s)
        {
	        try
	        {
		        bool part1 = s.Poll(1000, SelectMode.SelectRead);
		        bool part2 = (s.Available == 0);
		        if (part1 && part2)
			        return false;
		        else
			        return true;
	        }
	        catch
	        {
		        return false;
	        }
        }

	    public void Dispose()
	    {
			Stop();

		   // NetworkProcessing?.Wait();
			//NetworkProcessing?.Dispose();
		    NetworkProcessing = null;
		    ClearOutQueue(PacketWriteQueue);

			//NetworkWriting?.Wait();
			//NetworkWriting?.Dispose();
			NetworkWriting = null;
			//PacketWriteQueue?.Dispose();

		  //  ClearOutQueue(HandlePacketQueue);

			//PacketHandling?.Wait();
			//PacketHandling?.Dispose();
		//	PacketHandling = null;

			//HandlePacketQueue?.Dispose();

		    CancellationToken?.Dispose();

		    _readerStream?.Dispose();
		    _sendStream?.Dispose();
		    Socket?.Dispose();

			foreach (var state in UnhandledPacketsFilter.ToArray())
		    {
			    foreach (var p in state.Value)
			    {
					Log.Warn($"({state.Key}) unhandled: 0x{p.Key:X2} ({(state.Key == ConnectionState.Play ? MCPacketFactory.GetPlayPacketName(p.Key) : "Unknown")}) * {p.Value}");
			    }
		    }

			UnhandledPacketsFilter.Clear();
		}

	    private void ClearOutQueue<TType>(BlockingCollection<TType> collection)
	    {
			collection.CompleteAdding();
		    while (collection.TryTake(out var _, 0)) {};
	    }

	    private struct EnqueuedPacket
	    {
		    public Packet Packet;
		    public bool Encryption;
		    public bool CompressionEnabled;

		    public EnqueuedPacket(Packet packet, bool encryption, bool compression)
		    {
			    Packet = packet;
			    Encryption = encryption;
			    CompressionEnabled = compression;
		    }
	    }
    }

    internal struct SendData
    {
        public byte[] Buffer;
        public DateTime Time;

        public SendData(byte[] buffer)
        {
            Buffer = buffer;
            Time = DateTime.UtcNow;
        }
    }
}
