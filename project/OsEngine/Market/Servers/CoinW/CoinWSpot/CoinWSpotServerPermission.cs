using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.CoinW.CoinWSpot
{
    public class CoinWSpotServerPermission : IServerPermission
    {
        public ServerType ServerType
        {
            get { return ServerType.CoinWSpot; }
        }

        public bool DataFeedTf1SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf2SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf5SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf10SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf15SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf20SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf30SecondCanLoad => throw new NotImplementedException();

        public bool DataFeedTf1MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf2MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf5MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf10MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf15MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf30MinuteCanLoad => throw new NotImplementedException();

        public bool DataFeedTf1HourCanLoad => throw new NotImplementedException();

        public bool DataFeedTf2HourCanLoad => throw new NotImplementedException();

        public bool DataFeedTf4HourCanLoad => throw new NotImplementedException();

        public bool DataFeedTfTickCanLoad => throw new NotImplementedException();

        public bool DataFeedTfMarketDepthCanLoad => throw new NotImplementedException();

        public bool MarketOrdersIsSupport => throw new NotImplementedException();

        public bool IsCanChangeOrderPrice => throw new NotImplementedException();

        public bool IsUseLotToCalculateProfit => throw new NotImplementedException();

        public TimeFramePermission TradeTimeFramePermission => throw new NotImplementedException();

        public int WaitTimeSecondsAfterFirstStartToSendOrders
        {
            get { return 1; }
        }

        public bool UseStandartCandlesStarter => throw new NotImplementedException();

        public bool ManuallyClosePositionOnBoard_IsOn => throw new NotImplementedException();

        public string[] ManuallyClosePositionOnBoard_ValuesForTrimmingName => throw new NotImplementedException();

        public string[] ManuallyClosePositionOnBoard_ExceptionPositionNames => throw new NotImplementedException();

        public bool CanQueryOrdersAfterReconnect
        {
            get { return false; }
        }

        public bool CanQueryOrderStatus
        {
            get { return false; }
        }
    }
}
