using System;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Notifications;
using QuantConnect.Packets;
using Grapevine.Server;
using System.Collections.Generic;
using Grapevine.Interfaces.Server;
using Grapevine.Shared;
using Grapevine.Shared.Loggers;
using System.Collections.Concurrent;

namespace QuantConnect.Messaging
{
    /// <summary>
    /// Message handler that sends messages over http using SSE.
    /// </summary>
    public class SSEMessageHandler : IMessagingHandler
    {
        private RestServer _server;
        private AlgorithmNodePacket _job;
        private ConcurrentQueue<Packet> _queue;

        /// <summary>
        /// Gets or sets whether this messaging handler has any current subscribers.
        /// This is not used in this message handler.  Messages are sent via tcp as they arrive
        /// </summary>
        public bool HasSubscribers { get; set; }

        /// <summary>
        /// Initialize the messaging system
        /// </summary>
        public void Initialize()
        {
            var settings = new ServerSettings();
            settings.Port = Config.Get("desktop-http-port");           
            settings.PublicFolder = new PublicFolder(Config.Get("web-viewer-folder")) { Prefix = "/terminal" };
            settings.Logger = new ConsoleLogger(Grapevine.Interfaces.Shared.LogLevel.Trace);
            settings.Router.Register(ProcessPacket, HttpMethod.GET, "/events");
            _server = new RestServer(settings);
            _queue = new ConcurrentQueue<Packet>();
            _server.Start();
        }

        /// <summary>
        /// Set the user communication channel
        /// </summary>
        /// <param name="job"></param>
        public void SetAuthentication(AlgorithmNodePacket job)
        {
            _job = job;
            _queue.Enqueue(_job);
        }

        /// <summary>
        /// Send any notification with a base type of Notification.
        /// </summary>
        /// <param name="notification">The notification to be sent.</param>
        public void SendNotification(Notification notification)
        {
            var type = notification.GetType();
            if (type == typeof(NotificationEmail) || type == typeof(NotificationWeb) || type == typeof(NotificationSms))
            {
                Log.Error("Messaging.SendNotification(): Send not implemented for notification of type: " + type.Name);
                return;
            }
            notification.Send();
        }

        /// <summary>
        /// Send all types of packets
        /// </summary>
        public void Send(Packet packet)
        {
            ////Until we're loaded queue it up
            //if (!_server.IsListening)
            //{
            //    _queue.Enqueue(packet);
            //    return;
            //}

            _queue.Enqueue(packet);
        }

        /// <summary>
        /// Packet processing implementation
        /// </summary>
        public IHttpContext ProcessPacket(IHttpContext context)
        {
            // Send to server queue
            context.Response.ContentType = ContentType.CUSTOM_TEXT;
            context.Response.Advanced.ContentType = "text/event-stream";
            context.Response.KeepAlive = true;

            while (true)
            {
                try
                {
                    while (_queue.Count > 0)
                    {
                        Packet packet;
                        if (!_queue.TryDequeue(out packet))
                            break;
                        SendResponse(context.Response, packet);
                        if (StreamingApi.IsEnabled)
                        {
                            StreamingApi.Transmit(_job.UserId, _job.Channel, packet);
                        }
                    }
                }
                catch(Exception ex)
                {
                    _server.Logger.Error(ex);
                    break;
                }
            }

            // close connection
            context.Response.Advanced.Close();
            return context;
        }

        private void SendResponse(IHttpResponse response, Packet packet)
        {
            var payload = JsonConvert.SerializeObject(packet);
            SendResponse(response, $"event: {packet.Type}\ndata: {payload}\n\n");
        }

        private void SendResponse(IHttpResponse response, string msg)
        {
            var data = response.ContentEncoding.GetBytes(msg);
            response.Advanced.OutputStream.Write(data, 0, data.Length);
            response.Advanced.OutputStream.Flush();
        }

        public void Dispose()
        {
            _server.Stop();
        }
    }
}
