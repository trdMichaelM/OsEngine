using Newtonsoft.Json;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.CoinW.CoinWSpot.Json;
using OsEngine.Market.Servers.Entity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using WebSocket4Net;

namespace OsEngine.Market.Servers.CoinW.CoinWSpot
{
    public class CoinWSpotServer : AServer
    {
        public CoinWSpotServer()
        {
            CoinWSpotServerRealization realization = new CoinWSpotServerRealization();
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamPassphrase, "");
        }
    }

    public class CoinWSpotServerRealization : IServerRealization
    {
        private readonly string baseUrl = "https://www.coinw.com";
        private readonly HttpClient httpClient = new HttpClient();

        private WebSocketInformation webSocketInformation;
        private WebSocket webSocket;
        private ConcurrentQueue<string> webSocketMessages;
        private string socketLocker = "webSocketLockerCoinW";

        public CoinWSpotServerRealization()
        {
            ServerStatus = ServerConnectStatus.Disconnect;

            // Threads should be here...
            Thread sendPingWebSocketThread = new Thread(SendPingWebSocket);
            sendPingWebSocketThread.IsBackground = true;
            sendPingWebSocketThread.Name = "SendPingWebSocket";
            sendPingWebSocketThread.Start();
        }

        private void SendPingWebSocket()
        {
            int pingInterval = 10_000;
            while (true)
            {
                try
                {
                    if (webSocketInformation != null)
                    {
                        pingInterval = Convert.ToInt32(webSocketInformation.pingInterval);
                    }

                    Thread.Sleep(pingInterval);

                    if (webSocket != null && webSocket.State == WebSocketState.Open)
                    {
                        lock (socketLocker)
                        {
                            webSocket.Send("2"); // Socket.IO
                        }
                    }
                }
                catch (Exception e)
                {
                    SendLogMessage(e.ToString(), LogMessageType.Error);
                }
            }
        }

        public DateTime ServerTime { get; set; }

        public void Connect()
        {
            publicKey = ((ServerParameterString)ServerParameters[0]).Value;
            secretKey = ((ServerParameterPassword)ServerParameters[1]).Value;
            passphrase = ((ServerParameterPassword)ServerParameters[2]).Value;

            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(passphrase))
            {
                SendLogMessage("Can`t run CoinW Spot connector. No keys or passphrase", LogMessageType.Error);
                return;
            }

            try
            {
                HttpResponseMessage responseMessage = httpClient.GetAsync(baseUrl + "/pusher/public-token").Result;

                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseContent = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<WebSocketInformation> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<WebSocketInformation>>(responseContent);

                    if (responseMessageRest == null)
                    {
                        SendLogMessage("Can`t run CoinW Spot connector. No identity information required for WebSocket connection", LogMessageType.Error);
                        return;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage("Can`t run CoinW Spot connector. " + "Code: " + responseMessageRest.code, LogMessageType.Error);
                        return;
                    }

                    webSocketInformation = responseMessageRest.data;

                    webSocketMessages = new ConcurrentQueue<string>();

                    CreateWebSocketConnection();
                }
                else
                {
                    SendLogMessage("Connection cannot be open. CoinW. Error request", LogMessageType.Error);
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
                SendLogMessage("Connection can be open. CoinW. Error request", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        private void CreateWebSocketConnection()
        {
            string endpoint = webSocketInformation.endpoint;
            string token = webSocketInformation.token;
            //string pingInterval = webSocketInformation.pingInterval;

            string url = $"{endpoint}/socket.io/?token={token}&EIO=3&transport=websocket";

            webSocket = new WebSocket(url);
            //webSocket.EnableAutoSendPing = true;
            //webSocket.AutoSendPingInterval = Convert.ToInt32(pingInterval);

            webSocket.Opened += WebSocket_Opened;
            webSocket.Closed += WebSocket_Closed;
            webSocket.MessageReceived += WebSocket_MessageReceived;
            webSocket.Error += WebSocket_Error;

            webSocket.Open();
        }

        private void WebSocket_Opened(object sender, EventArgs e)
        {
            // Server uses Socket.IO https://socket.io/
            webSocket.Send("2probe");
            webSocket.Send("5");

            SendLogMessage("Websocket connection open", LogMessageType.System);

            ServerStatus = ServerConnectStatus.Connect;
            ConnectEvent();
        }

        private void WebSocket_Closed(object sender, EventArgs e)
        {
            if (ServerStatus != ServerConnectStatus.Disconnect)
            {
                SendLogMessage("Connection Closed by CoinW. WebSocket Closed Event", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        private void WebSocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (e == null || webSocketMessages == null)
                {
                    return;
                }

                string message = e.Message;
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }
                if (message.StartsWith("3")) // Pong message Socket.IO
                {
                    return;
                }
                if (message.StartsWith("42")) // Event message Socket.IO
                {
                    webSocketMessages.Enqueue(message);
                }
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            if (e.Exception != null)
            {
                SendLogMessage(e.Exception.ToString(), LogMessageType.Error);
            }
        }

        public void Dispose()
        {

        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;

        public List<IServerParameter> ServerParameters { get; set; }

        private string publicKey;

        private string secretKey;

        private string passphrase;

        public ServerType ServerType
        {
            get { return ServerType.CoinWSpot; }
        }

        public ServerConnectStatus ServerStatus { get; set; }

        public event Action<List<Security>> SecurityEvent;
        public event Action<List<Portfolio>> PortfolioEvent;
        public event Action<MarketDepth> MarketDepthEvent;
        public event Action<Trade> NewTradesEvent;
        public event Action<Order> MyOrderEvent;
        public event Action<MyTrade> MyTradeEvent;

        public void CancelAllOrders()
        {
            throw new NotImplementedException();
        }

        public void CancelAllOrdersToSecurity(Security security)
        {
            throw new NotImplementedException();
        }

        public void CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            throw new NotImplementedException();
        }

        public void GetAllActivOrders()
        {
            throw new NotImplementedException();
        }

        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            throw new NotImplementedException();
        }

        public void GetOrderStatus(Order order)
        {
            throw new NotImplementedException();
        }

        public void GetPortfolios()
        {

        }

        public void GetSecurities()
        {
            try
            {
                HttpResponseMessage responseMessage = httpClient.GetAsync(baseUrl + "/api/v1/public?command=returnSymbol").Result;

                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseContent = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<List<TradingPair>> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<List<TradingPair>>>(responseContent);

                    if (responseMessageRest == null)
                    {
                        SendLogMessage("No securities from CoinW.", LogMessageType.Error);
                        return;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage("No securities from CoinW. " + "Code: " + responseMessageRest.code, LogMessageType.Error);
                        return;
                    }

                    UpdateSecurity(responseMessageRest.data);
                }
                else
                {
                    SendLogMessage("No securities from CoinW. " + "Code: " + responseMessage.StatusCode, LogMessageType.Error);
                }
            }
            catch (Exception e)
            {
                SendLogMessage(e.ToString(), LogMessageType.Error);
            }
        }

        private void UpdateSecurity(List<TradingPair> tradingPair)
        {
            List<Security> securities = new List<Security>();

            Security security = new Security();
            security.Exchange = ServerType.CoinWSpot.ToString();
            //security.Lot = 
        }

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        public void SendOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void Subscrible(Security security)
        {
            throw new NotImplementedException();
        }

        #region Log

        public event Action<string, LogMessageType> LogMessageEvent;

        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent(message, messageType);
        }

        #endregion
    }
}