using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Tab.Internal;
using System;
using System.Collections.Generic;

namespace OsEngine.Robots.MarketMaker
{
    [Bot("MarketMakerStakan3")]
    public class MarketMakerStakan3 : BotPanel
    {
        private BotTabSimple _tab;
        private readonly NonTradePeriods _nonTradePeriods;
        private readonly StrategyParameterString _nonTradeMode;
        private readonly StrategyParameterButton _nonTradeButton;

        private StrategyParameterString _regime;
        private StrategyParameterString _buyMode;
        private StrategyParameterDecimal _buyTotalVolume;
        private StrategyParameterString _buyVolumeMode;
        private StrategyParameterDecimal _levelVolumePercent;
        private StrategyParameterString _sellVolumeMode;
        private StrategyParameterDecimal _sellPercent;
        private StrategyParameterDecimal _wallMinVolume;
        private StrategyParameterDecimal _wallMedianFactor;
        private StrategyParameterInt _confirmTicks;

        private bool _wallNotFoundLogged;
        private decimal _wallCandidatePrice;
        private int _wallCandidateCount;

        private bool _ordersClearedInOffMode = false;

        private readonly Dictionary<decimal, decimal> _sellForeignBasis = new Dictionary<decimal, decimal>();
        private decimal _sellPapersBase = -1m;
        private readonly Queue<decimal> _sellPapersHistory = new Queue<decimal>();

        private readonly Dictionary<int, DateTime> _ordersOnCancel = new Dictionary<int, DateTime>();
        private static readonly TimeSpan CancelRetryTimeout = TimeSpan.FromSeconds(60);

        private class PendingPriceChange
        {
            public decimal TargetPrice;
            public DateTime Time;
        }

        private readonly Dictionary<int, PendingPriceChange> _ordersOnPriceChange = new Dictionary<int, PendingPriceChange>();

        private readonly object _planLock = new object();

        private DateTime _lastProcessTime = DateTime.MinValue;
        private static readonly TimeSpan MinProcessInterval = TimeSpan.FromMilliseconds(100);

        private DateTime _lastOwnOrdersChange = DateTime.MinValue;
        private static readonly TimeSpan OwnOrdersSettleTime = TimeSpan.FromMilliseconds(400);

        private DateTime _onTime = DateTime.MinValue;

        private DateTime _lastPortfolioErrorLog = DateTime.MinValue;
        private static readonly TimeSpan PortfolioErrorLogInterval = TimeSpan.FromSeconds(60);

        private readonly HashSet<int> _restoredPositionNumbers = new HashSet<int>();

        private bool _portfolioTimingLogged;

        public MarketMakerStakan3(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _nonTradePeriods = new NonTradePeriods(name);
            _nonTradeMode = CreateParameter("В неторговый период", "Только закрытие позиций", new[] { "Только закрытие позиций", "Пауза" }, "Неторговые периоды");
            _nonTradeButton = CreateParameterButton("Неторговые периоды", "Неторговые периоды");
            _nonTradeButton.UserClickOnButtonEvent += () => _nonTradePeriods.ShowDialog();

            List<Position> restoredPositions = _tab.PositionsAll;

            if (restoredPositions != null)
            {
                for (int i = 0; i < restoredPositions.Count; i++)
                {
                    _restoredPositionNumbers.Add(restoredPositions[i].Number);
                }
            }

            _tab.ManualPositionSupport.DisableManualSupport();
            _tab.ManualPositionSupport.OrderTypeTime = OrderTypeTime.GTC;
            _tab.ManualPositionSupport.Save();

            _regime = CreateParameter("Режим", "Off", new[] { "Off", "On" });
            _buyMode = CreateParameter("Режим покупки", "Распределение сверху вниз", new[] { "Один уровень (макс. объём)", "Распределение сверху вниз", "Перед большой заявкой" });
            _buyTotalVolume = CreateParameter("Суммарный объём покупки (лоты или % портфеля)", 100.0m, 0.0001m, 100000.0m, 0.0001m);
            _buyVolumeMode = CreateParameter("Режим объёма покупки", "% от стоимости портфеля", new[] { "Фиксированный", "% от стоимости портфеля", "Максимум без маржи" });
            _levelVolumePercent = CreateParameter("Процент от объёма уровня", 100.0m, 1.0m, 1000.0m, 1.0m);
            _sellVolumeMode = CreateParameter("Режим объёма продажи", "% от бумаг в портфеле", new[] { "% от бумаг в портфеле", "% от всего портфеля" });
            _sellPercent = CreateParameter("Процент продажи", 100.0m, 1.0m, 100.0m, 1.0m);
            _wallMinVolume = CreateParameter("Мин. объём стены", 100000.0m, 0.0m, 100000000.0m, 1.0m);
            _wallMedianFactor = CreateParameter("Превышение над медианой, раз", 3.0m, 0.0m, 1000.0m, 0.1m);
            _confirmTicks = CreateParameter("Проверка моргания, тиков", 3, 1, 20, 1);

            _tab.MarketDepthUpdateEvent += _tab_MarketDepthUpdateEvent;
            _tab.OrderUpdateEvent += _tab_OrderUpdateEvent;
            DeleteEvent += MarketMakerStakan3_DeleteEvent;
        }

        
        private bool ScheduledTradeAllowed()
        {
            DateTime time = _tab.TimeServerCurrent;
            return time != DateTime.MinValue && _nonTradePeriods.CanTradeThisTime(time);
        }
        private bool SchedulePaused() => _nonTradeMode.ValueString == "Пауза" && !ScheduledTradeAllowed();
        private bool ScheduleCloseOnly() => _nonTradeMode.ValueString != "Пауза" && !ScheduledTradeAllowed();

        private decimal SignedBoardPosition()
        {
            if (_tab.Security == null || _tab.Portfolio == null) return 0;
            List<PositionOnBoard> rows = _tab.Portfolio.GetPositionOnBoard();
            PositionOnBoard found = null;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                PositionOnBoard row = rows[i];
                if (row == null || row.SecurityNameCode != _tab.Security.Name ||
                    (!string.IsNullOrEmpty(row.SecurityNameClass) && row.SecurityNameClass != _tab.Security.NameClass))
                    continue;
                if (found != null) return 0;
                found = row;
            }
            return found == null ? 0 : found.ValueCurrent;
        }

        // The plan is restricted to the direction that reduces current exposure.
        // When flat this returns an empty plan and the existing orders are cancelled.
        private List<PlanOrder> CloseOnlyPlan(List<PlanOrder> plan)
        {
            decimal net = SignedBoardPosition();
            Side closeSide = net > 0 ? Side.Sell : Side.Buy;
            decimal remaining = Math.Abs(net);
            List<PlanOrder> restricted = new List<PlanOrder>();
            if (remaining <= 0) return restricted;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Side != closeSide || remaining <= 0) continue;
                decimal volume = Math.Min(Math.Max(0, plan[i].Volume), remaining);
                if (volume <= 0) continue;
                restricted.Add(new PlanOrder { Side = closeSide, Price = plan[i].Price, Volume = volume });
                remaining -= volume;
            }
            return restricted;
        }

        private decimal RemainingClosingCapacity(Side side)
        {
            decimal net = SignedBoardPosition();
            decimal capacity = side == Side.Buy ? Math.Max(0, -net) : Math.Max(0, net);
            List<Order> live = GetMyActiveOrders();
            for (int i = 0; i < live.Count; i++)
                if (live[i].Side == side) capacity -= Math.Max(0m, live[i].Volume - live[i].VolumeExecute);
            return Math.Max(0, capacity);
        }

        public override string GetNameStrategyType()
        {
            return "MarketMakerStakan3";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }

        private void MarketMakerStakan3_DeleteEvent()
        {
            _tab.MarketDepthUpdateEvent -= _tab_MarketDepthUpdateEvent;
            _tab.OrderUpdateEvent -= _tab_OrderUpdateEvent;
        }

        private class PlanOrder
        {
            public Side Side;
            public decimal Price;
            public decimal Volume;
        }

        private void _tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            try
            {
                // Pause mode must preserve positions and outstanding orders exactly as they are.
                if (SchedulePaused()) return;
                if (StartProgram == StartProgram.IsOsOptimizer)
                {
                    return;
                }

                lock (_planLock)
                {
                    if (_regime.ValueString == "On")
                    {
                        if (_onTime == DateTime.MinValue)
                        {
                            _onTime = DateTime.Now;
                            SendNewLogMessage("Робот включён в режим \"On\"", LogMessageType.Trade);
                        }

                        if (!_portfolioTimingLogged)
                        {
                            Portfolio portfolio = _tab.Portfolio;

                            if (portfolio != null &&
                                (portfolio.ValueCurrent > 0 ||
                                 (portfolio.GetPositionOnBoard() != null &&
                                  portfolio.GetPositionOnBoard().Count > 0)))
                            {
                                _portfolioTimingLogged = true;
                                SendNewLogMessage("Портфель получен через " +
                                                  (DateTime.Now - _onTime).TotalSeconds.ToString("F1") +
                                                  " сек после включения", LogMessageType.Trade);
                            }
                        }
                    }

                    if (_regime.ValueString == "Off")
                    {
                        CancelAllActiveOrders(!_ordersClearedInOffMode);
                        _ordersClearedInOffMode = true;
                        _sellForeignBasis.Clear();
                        _sellPapersBase = -1m;
                        _sellPapersHistory.Clear();
                        return;
                    }

                    _ordersClearedInOffMode = false;

                    if (StartProgram == StartProgram.IsOsTrader &&
                        _lastProcessTime + MinProcessInterval > DateTime.Now)
                    {
                        return;
                    }

                    _lastProcessTime = DateTime.Now;

                    if (marketDepth == null)
                    {
                        return;
                    }

                    MarketDepth md = marketDepth.GetCopy();

                    if (md.Asks == null || md.Asks.Count == 0 ||
                        md.Bids == null || md.Bids.Count == 0)
                    {
                        return;
                    }

                    if (_tab.Security == null)
                    {
                        return;
                    }

                    if (_tab.Security.VolumeStep <= 0)
                    {
                        SendNewLogMessage("VolumeStep бумаги равен нулю — объём заявки округлить невозможно", LogMessageType.Error);
                        return;
                    }

                    List<Order> activeOrders = GetMyActiveOrders();
                    List<PlanOrder> newPlan = BuildPlan(md, activeOrders);
                    if (ScheduleCloseOnly())
                    {
                        decimal net = SignedBoardPosition();
                        Side closeSide = net > 0 ? Side.Sell : Side.Buy;
                        bool mustCancelOpeningOrders = false;
                        for (int i = 0; i < activeOrders.Count; i++)
                        {
                            if (net == 0 || activeOrders[i].Side != closeSide)
                            {
                                CancelOrderSafe(activeOrders[i]);
                                mustCancelOpeningOrders = true;
                            }
                        }
                        if (mustCancelOpeningOrders) return;
                        newPlan = CloseOnlyPlan(newPlan);
                    }

                    if (OrdersMatchPlan(activeOrders, newPlan))
                    {
                        return;
                    }

                    ApplyPlan(newPlan, activeOrders, md);
                }
            }
            catch (Exception e)
            {
                SendNewLogMessage("Ошибка в обработке стакана: " + e, LogMessageType.Error);
            }
        }

        private void _tab_OrderUpdateEvent(Order order)
        {
            if (order == null)
            {
                return;
            }

            lock (_planLock)
            {
                if (order.State == OrderStateType.Cancel ||
                    order.State == OrderStateType.Done ||
                    order.State == OrderStateType.Fail)
                {
                    _ordersOnCancel.Remove(order.NumberUser);
                    _ordersOnPriceChange.Remove(order.NumberUser);
                }
                else
                {
                    PendingPriceChange pending;

                    if (_ordersOnPriceChange.TryGetValue(order.NumberUser, out pending) &&
                        order.Price == pending.TargetPrice)
                    {
                        _ordersOnPriceChange.Remove(order.NumberUser);
                    }
                }
            }

            if (order.State == OrderStateType.Fail)
            {
                SendNewLogMessage("Заявка " + order.Side + " " + order.Price +
                                  " отклонена биржей (не считаем её активной)",
                                  LogMessageType.Trade);
            }
        }

        private void CancelOrderSafe(Order order)
        {
            if (order == null)
            {
                return;
            }

            lock (_planLock)
            {
                if (order.State != OrderStateType.Active &&
                    order.State != OrderStateType.Partial &&
                    order.State != OrderStateType.Pending)
                {
                    return;
                }

                DateTime cancelTime;

                if (_ordersOnCancel.TryGetValue(order.NumberUser, out cancelTime) &&
                    cancelTime + CancelRetryTimeout > DateTime.Now)
                {
                    return;
                }

                _ordersOnCancel[order.NumberUser] = DateTime.Now;
                _tab.CloseOrder(order);
            }
        }

        private List<PlanOrder> BuildPlan(MarketDepth md, List<Order> activeOrders)
        {
            List<PlanOrder> plan = new List<PlanOrder>();

            BuildBuyPlan(md, plan, activeOrders);
            BuildSellPlan(md, plan, activeOrders);

            return plan;
        }

        private void BuildBuyPlan(MarketDepth md, List<PlanOrder> plan, List<Order> activeOrders)
        {
            if (_buyMode.ValueString != "Перед большой заявкой")
            {
                _wallCandidatePrice = 0;
                _wallCandidateCount = 0;
                _wallNotFoundLogged = false;
            }

            decimal totalBuyVolume = GetBuyTotalVolume(md);

            if (totalBuyVolume <= 0)
            {
                return;
            }

            List<decimal> foreignVolumes = GetForeignVolumes(md.Bids, Side.Buy, activeOrders);

            if (_buyMode.ValueString == "Один уровень (макс. объём)")
            {
                int bestIndex = -1;
                decimal bestVolume = -1m;

                for (int i = 0; i < foreignVolumes.Count; i++)
                {
                    if (foreignVolumes[i] > bestVolume)
                    {
                        bestVolume = foreignVolumes[i];
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    return;
                }

                decimal price = RoundPrice(Convert.ToDecimal(md.Bids[bestIndex].Price), Side.Buy);
                decimal volume = RoundVolume(totalBuyVolume);

                if (price > 0 && volume > 0)
                {
                    plan.Add(new PlanOrder { Side = Side.Buy, Price = price, Volume = volume });
                }
            }
            else if (_buyMode.ValueString == "Перед большой заявкой")
            {
                BuildWallBuyPlan(md, plan, totalBuyVolume, foreignVolumes);
            }
            else
            {
                decimal rest = totalBuyVolume;

                for (int i = 0; i < md.Bids.Count && rest > 0; i++)
                {
                    decimal price = RoundPrice(Convert.ToDecimal(md.Bids[i].Price), Side.Buy);

                    if (price <= 0)
                    {
                        continue;
                    }

                    decimal want = _levelVolumePercent.ValueDecimal / 100m * foreignVolumes[i];

                    if (want > rest)
                    {
                        want = rest;
                    }

                    decimal volume = RoundVolumeDown(want);

                    if (volume <= 0)
                    {
                        continue;
                    }

                    plan.Add(new PlanOrder { Side = Side.Buy, Price = price, Volume = volume });
                    rest -= volume;
                }

                ApplyStandingOrdersFloor(plan, activeOrders, Side.Buy);
            }
        }

        private void BuildWallBuyPlan(MarketDepth md, List<PlanOrder> plan, decimal totalBuyVolume, List<decimal> foreignVolumes)
        {
            decimal wallPrice = FindWallPrice(foreignVolumes, md);

            if (wallPrice <= 0)
            {
                _wallCandidatePrice = 0;
                _wallCandidateCount = 0;

                if (!_wallNotFoundLogged)
                {
                    _wallNotFoundLogged = true;
                    SendNewLogMessage("Режим \"Перед большой заявкой\": стена не найдена", LogMessageType.Trade);
                }

                return;
            }

            _wallNotFoundLogged = false;

            if (wallPrice == _wallCandidatePrice)
            {
                _wallCandidateCount++;
            }
            else
            {
                _wallCandidatePrice = wallPrice;
                _wallCandidateCount = 1;
            }

            if (_wallCandidateCount < _confirmTicks.ValueInt)
            {
                return;
            }

            if (md.Asks == null || md.Asks.Count == 0)
            {
                return;
            }

            decimal bestAsk = Convert.ToDecimal(md.Asks[0].Price);
            decimal price = RoundPrice(wallPrice + _tab.Security.PriceStep, Side.Buy);

            if (price <= 0 || price >= bestAsk)
            {
                return;
            }

            decimal volume = RoundVolume(totalBuyVolume);

            if (volume > 0)
            {
                plan.Add(new PlanOrder { Side = Side.Buy, Price = price, Volume = volume });
            }
        }

        private decimal FindWallPrice(List<decimal> foreignVolumes, MarketDepth md)
        {
            if (md.Bids == null || md.Bids.Count < 2)
            {
                return 0;
            }

            decimal median = MedianVolumes(foreignVolumes);

            if (median <= 0)
            {
                return 0;
            }

            for (int i = 0; i < foreignVolumes.Count; i++)
            {
                decimal volume = foreignVolumes[i];

                if (volume >= _wallMinVolume.ValueDecimal &&
                    volume >= _wallMedianFactor.ValueDecimal * median)
                {
                    return Convert.ToDecimal(md.Bids[i].Price);
                }
            }

            return 0;
        }

        private decimal MedianVolumes(List<decimal> volumes)
        {
            if (volumes == null || volumes.Count < 2)
            {
                return 0;
            }

            List<decimal> sorted = new List<decimal>(volumes);
            sorted.Sort();

            int count = sorted.Count;

            if (count % 2 == 1)
            {
                return sorted[count / 2];
            }

            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2m;
        }

        private void BuildSellPlan(MarketDepth md, List<PlanOrder> plan, List<Order> activeOrders)
        {
            decimal rest = GetSellPlanVolume(md, activeOrders);

            if (rest <= 0)
            {
                return;
            }

            List<decimal> foreignVolumes = GetForeignVolumes(md.Asks, Side.Sell, activeOrders);

            _sellPapersHistory.Enqueue(rest);

            while (_sellPapersHistory.Count > _confirmTicks.ValueInt)
            {
                _sellPapersHistory.Dequeue();
            }

            decimal papersBase = decimal.MaxValue;

            foreach (decimal value in _sellPapersHistory)
            {
                if (value < papersBase)
                {
                    papersBase = value;
                }
            }

            if (_sellPapersBase != papersBase)
            {
                _sellPapersBase = papersBase;
                _sellForeignBasis.Clear();
            }

            rest = _sellPapersBase;

            Dictionary<decimal, decimal> newBasis = new Dictionary<decimal, decimal>();

            for (int i = 0; i < md.Asks.Count && rest > 0; i++)
            {
                decimal price = RoundPrice(Convert.ToDecimal(md.Asks[i].Price), Side.Sell);

                if (price <= 0)
                {
                    continue;
                }

                decimal basis = foreignVolumes[i];
                decimal oldBasis;

                if (_sellForeignBasis.TryGetValue(price, out oldBasis) && oldBasis < basis)
                {
                    basis = oldBasis;
                }

                newBasis[price] = basis;

                decimal want = _levelVolumePercent.ValueDecimal / 100m * basis;

                if (want > rest)
                {
                    want = rest;
                }

                decimal volume = RoundVolumeDown(want);

                if (volume <= 0)
                {
                    continue;
                }

                plan.Add(new PlanOrder { Side = Side.Sell, Price = price, Volume = volume });
                rest -= volume;
            }

            _sellForeignBasis.Clear();

            foreach (KeyValuePair<decimal, decimal> pair in newBasis)
            {
                _sellForeignBasis[pair.Key] = pair.Value;
            }

            ApplyStandingOrdersFloor(plan, activeOrders, Side.Sell);
        }

        private void ApplyStandingOrdersFloor(List<PlanOrder> plan, List<Order> activeOrders, Side side)
        {
            if (_lastOwnOrdersChange + OwnOrdersSettleTime < DateTime.Now)
            {
                return;
            }

            for (int i = 0; i < activeOrders.Count; i++)
            {
                Order order = activeOrders[i];

                if (order.Side != side)
                {
                    continue;
                }

                decimal myRest = order.Volume - order.VolumeExecute;

                if (myRest <= 0)
                {
                    continue;
                }

                for (int j = 0; j < plan.Count; j++)
                {
                    if (plan[j].Side == side && plan[j].Price == order.Price)
                    {
                        if (plan[j].Volume < myRest)
                        {
                            plan[j].Volume = myRest;
                        }

                        break;
                    }
                }
            }
        }

        private decimal GetSellPlanVolume(MarketDepth md, List<Order> activeOrders)
        {
            if (_sellVolumeMode.ValueString != "% от бумаг в портфеле")
            {
                return GetSellTotalVolume(md);
            }

            decimal free = GetSellTotalVolume(md);
            decimal reserved = 0;

            for (int i = 0; i < activeOrders.Count; i++)
            {
                if (activeOrders[i].Side == Side.Sell)
                {
                    reserved += activeOrders[i].Volume - activeOrders[i].VolumeExecute;
                }
            }

            return free + reserved * _sellPercent.ValueDecimal / 100m;
        }

        private decimal GetBuyTotalVolume(MarketDepth md)
        {
            if (_buyVolumeMode.ValueString == "Фиксированный")
            {
                return _buyTotalVolume.ValueDecimal;
            }

            Portfolio portfolio = _tab.Portfolio;

            if (portfolio == null)
            {
                LogPortfolioUnavailable("Портфель недоступен — объём покупки не считаем");
                return 0;
            }

            decimal bestBid = Convert.ToDecimal(md.Bids[0].Price);

            if (bestBid <= 0)
            {
                return 0;
            }

            decimal moneyForBuy;

            if (_buyVolumeMode.ValueString == "% от стоимости портфеля")
            {
                moneyForBuy = portfolio.ValueCurrent * _buyTotalVolume.ValueDecimal / 100m;

                if (moneyForBuy <= 0)
                {
                    return 0;
                }

                return moneyForBuy / bestBid;
            }

            moneyForBuy = GetFreeMoney(portfolio);

            if (moneyForBuy <= 0)
            {
                return 0;
            }

            return moneyForBuy / bestBid;
        }

        private decimal GetFreeMoney(Portfolio portfolio)
        {
            List<PositionOnBoard> positionsOnBoard = portfolio.GetPositionOnBoard();

            if (positionsOnBoard != null)
            {
                for (int i = 0; i < positionsOnBoard.Count; i++)
                {
                    if (positionsOnBoard[i].SecurityNameCode == "rub")
                    {
                        return positionsOnBoard[i].ValueCurrent;
                    }
                }
            }

            decimal free = portfolio.ValueCurrent - portfolio.ValueBlocked;

            if (free < 0)
            {
                free = 0;
            }

            return free;
        }

        private decimal GetSellTotalVolume(MarketDepth md)
        {
            Portfolio portfolio = _tab.Portfolio;

            if (portfolio == null)
            {
                LogPortfolioUnavailable("Портфель недоступен — объём продажи не считаем");
                return 0;
            }

            if (_sellVolumeMode.ValueString == "% от бумаг в портфеле")
            {
                List<PositionOnBoard> positionsOnBoard = portfolio.GetPositionOnBoard();

                if (positionsOnBoard == null)
                {
                    return 0;
                }

                decimal securityQty = 0;

                for (int i = 0; i < positionsOnBoard.Count; i++)
                {
                    if (positionsOnBoard[i].SecurityNameCode == _tab.Security.Name)
                    {
                        securityQty = positionsOnBoard[i].ValueCurrent - positionsOnBoard[i].ValueBlocked;

                        if (securityQty < 0)
                        {
                            securityQty = 0;
                        }

                        break;
                    }
                }

                return securityQty * _sellPercent.ValueDecimal / 100m;
            }
            else
            {
                decimal bestAsk = Convert.ToDecimal(md.Asks[0].Price);

                if (bestAsk <= 0)
                {
                    return 0;
                }

                decimal moneyForSell = portfolio.ValueCurrent * _sellPercent.ValueDecimal / 100m;

                return moneyForSell / bestAsk;
            }
        }

        private void LogPortfolioUnavailable(string message)
        {
            if (_lastPortfolioErrorLog + PortfolioErrorLogInterval > DateTime.Now)
            {
                return;
            }

            _lastPortfolioErrorLog = DateTime.Now;
            SendNewLogMessage(message, LogMessageType.Error);
        }

        private List<decimal> GetForeignVolumes(List<MarketDepthLevel> levels, Side side, List<Order> myActiveOrders)
        {
            List<decimal> result = new List<decimal>();

            for (int i = 0; i < levels.Count; i++)
            {
                decimal foreign = side == Side.Buy
                    ? Convert.ToDecimal(levels[i].Bid)
                    : Convert.ToDecimal(levels[i].Ask);

                result.Add(foreign);
            }

            for (int i = 0; i < myActiveOrders.Count; i++)
            {
                Order order = myActiveOrders[i];

                if (order.Side != side)
                {
                    continue;
                }

                for (int j = 0; j < levels.Count; j++)
                {
                    if (Convert.ToDecimal(levels[j].Price) == order.Price)
                    {
                        decimal myRest = order.Volume - order.VolumeExecute;
                        result[j] -= myRest;

                        if (result[j] < 0)
                        {
                            result[j] = 0;
                        }

                        break;
                    }
                }
            }

            return result;
        }

        private void ApplyPlan(List<PlanOrder> plan, List<Order> activeOrders, MarketDepth md)
        {
            ApplyPlanSide(plan, activeOrders, Side.Buy, md);
            ApplyPlanSide(plan, activeOrders, Side.Sell, md);
        }

        private void ApplyPlanSide(List<PlanOrder> plan, List<Order> activeOrders, Side side, MarketDepth md)
        {
            List<PlanOrder> planSide = new List<PlanOrder>();

            for (int j = 0; j < plan.Count; j++)
            {
                if (plan[j].Side == side)
                {
                    planSide.Add(plan[j]);
                }
            }

            List<PlanOrder> mySide = new List<PlanOrder>();

            for (int i = 0; i < activeOrders.Count; i++)
            {
                Order order = activeOrders[i];

                if (order.Side != side)
                {
                    continue;
                }

                decimal rest = order.Volume - order.VolumeExecute;

                if (rest <= 0)
                {
                    continue;
                }

                bool merged = false;

                for (int m = 0; m < mySide.Count; m++)
                {
                    if (mySide[m].Price == order.Price)
                    {
                        mySide[m].Volume += rest;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    mySide.Add(new PlanOrder { Side = side, Price = order.Price, Volume = rest });
                }
            }

            mySide.Sort(delegate (PlanOrder a, PlanOrder b)
            {
                if (side == Side.Sell)
                {
                    return a.Price.CompareTo(b.Price);
                }

                return b.Price.CompareTo(a.Price);
            });

            int k = 0;

            while (k < mySide.Count && k < planSide.Count &&
                   mySide[k].Price == planSide[k].Price &&
                   VolumesMatch(planSide[k].Volume, mySide[k].Volume, side))
            {
                k++;
            }

            if (k == mySide.Count && k == planSide.Count)
            {
                return;
            }

            if (k == 0 && planSide.Count == 1 && mySide.Count == 1)
            {
                Order singleOrder = FindSingleActiveOrder(activeOrders, side);

                if (singleOrder != null &&
                    singleOrder.Price != planSide[0].Price &&
                    VolumesMatch(planSide[0].Volume, singleOrder.Volume - singleOrder.VolumeExecute, side) &&
                    TryChangeOrderPrice(singleOrder, planSide[0].Price))
                {
                    return;
                }
            }

            List<decimal> cancelPrices = new List<decimal>();

            for (int i = k; i < mySide.Count; i++)
            {
                cancelPrices.Add(mySide[i].Price);
            }

            for (int i = 0; i < activeOrders.Count; i++)
            {
                Order order = activeOrders[i];

                if (order.Side != side || !cancelPrices.Contains(order.Price))
                {
                    continue;
                }

                decimal restVolume = order.Volume - order.VolumeExecute;

                if (restVolume <= 0)
                {
                    continue;
                }

                SendNewLogMessage("Снимаем заявку " + order.Side + " " + order.Price +
                                  " объём " + restVolume + " (расходится с планом, позиция " + k + ")", LogMessageType.Trade);
                CancelOrderSafe(order);
                _lastOwnOrdersChange = DateTime.Now;
            }

            if (k >= planSide.Count)
            {
                return;
            }

            decimal? budget = null;
            decimal reserved = 0;

            for (int i = 0; i < activeOrders.Count; i++)
            {
                if (activeOrders[i].Side == side)
                {
                    reserved += activeOrders[i].Volume - activeOrders[i].VolumeExecute;
                }
            }

            for (int j = k; j < planSide.Count; j++)
            {
                decimal volumeToPlace = planSide[j].Volume;
                if (SchedulePaused()) return;
                if (ScheduleCloseOnly())
                {
                    volumeToPlace = Math.Min(volumeToPlace, RemainingClosingCapacity(side));
                    if (volumeToPlace <= 0) continue;
                }

                if (side == Side.Sell)
                {
                    if (budget == null)
                    {
                        budget = GetSellTotalVolume(md);
                    }

                    decimal canPlace = budget.Value - reserved;

                    if (volumeToPlace > canPlace)
                    {
                        volumeToPlace = RoundVolumeDown(canPlace);
                    }

                    if (volumeToPlace <= 0)
                    {
                        SendNewLogMessage("Пропускаем заявку Sell " + planSide[j].Price +
                                          " (нет свободных бумаг: резерв " + reserved +
                                          ", доступно " + budget.Value + ")", LogMessageType.Trade);
                        continue;
                    }

                    SendNewLogMessage("Выставляем заявку Sell " + planSide[j].Price +
                                      " объём " + volumeToPlace, LogMessageType.Trade);
                    _tab.SellAtLimit(volumeToPlace, planSide[j].Price);
                    reserved += volumeToPlace;
                    _lastOwnOrdersChange = DateTime.Now;
                }
                else
                {
                    if (_buyVolumeMode.ValueString != "Максимум без маржи")
                    {
                        if (budget == null)
                        {
                            budget = GetBuyTotalVolume(md);
                        }

                        decimal canPlace = budget.Value - reserved;

                        if (volumeToPlace > canPlace)
                        {
                            volumeToPlace = RoundVolumeDown(canPlace);
                        }

                        if (volumeToPlace <= 0)
                        {
                            SendNewLogMessage("Пропускаем заявку Buy " + planSide[j].Price +
                                              " (нет свободных денег: резерв " + reserved +
                                              ", доступно " + budget.Value + ")", LogMessageType.Trade);
                            continue;
                        }
                    }

                    SendNewLogMessage("Выставляем заявку Buy " + planSide[j].Price +
                                      " объём " + volumeToPlace, LogMessageType.Trade);
                    _tab.BuyAtLimit(volumeToPlace, planSide[j].Price);
                    reserved += volumeToPlace;
                    _lastOwnOrdersChange = DateTime.Now;
                }
            }
        }

        private Order FindSingleActiveOrder(List<Order> activeOrders, Side side)
        {
            Order found = null;
            int count = 0;

            for (int i = 0; i < activeOrders.Count; i++)
            {
                Order order = activeOrders[i];

                if (order.Side != side)
                {
                    continue;
                }

                if (order.Volume - order.VolumeExecute <= 0)
                {
                    continue;
                }

                count++;
                found = order;
            }

            if (count == 1)
            {
                return found;
            }

            return null;
        }

        private bool TryChangeOrderPrice(Order order, decimal newPrice)
        {
            if (_tab.EmulatorIsOn == false &&
                _tab.ServerIsSupportChangeOrderPrice == false)
            {
                return false;
            }

            if (order.State != OrderStateType.Active &&
                order.State != OrderStateType.Partial)
            {
                return false;
            }

            PendingPriceChange pending;

            if (_ordersOnPriceChange.TryGetValue(order.NumberUser, out pending))
            {
                if (pending.TargetPrice == newPrice &&
                    pending.Time + CancelRetryTimeout > DateTime.Now)
                {
                    return true;
                }
            }

            _ordersOnPriceChange[order.NumberUser] = new PendingPriceChange
            {
                TargetPrice = newPrice,
                Time = DateTime.Now
            };

            SendNewLogMessage("Переставляем заявку " + order.Side + " " + order.Price +
                              " -> " + newPrice, LogMessageType.Trade);
            _tab.ChangeOrderPrice(order, newPrice);
            _lastOwnOrdersChange = DateTime.Now;

            return true;
        }

        private void CancelAllActiveOrders(bool logOrders)
        {
            List<Order> activeOrders = GetMyActiveOrders();

            for (int i = 0; i < activeOrders.Count; i++)
            {
                if (logOrders)
                {
                    SendNewLogMessage("Режим Off: снимаем заявку " + activeOrders[i].Side +
                                      " " + activeOrders[i].Price, LogMessageType.Trade);
                }

                CancelOrderSafe(activeOrders[i]);
            }
        }

        private List<Order> GetMyActiveOrders()
        {
            List<Order> result = new List<Order>();

            List<Position> positions = _tab.PositionsAll;

            if (positions == null)
            {
                return result;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if (_restoredPositionNumbers.Contains(positions[i].Number))
                {
                    continue;
                }

                if (positions[i].State == PositionStateType.OpeningFail ||
                    positions[i].State == PositionStateType.Done)
                {
                    continue;
                }

                List<Order> openOrders = positions[i].OpenOrders;

                if (openOrders == null)
                {
                    continue;
                }

                for (int j = 0; j < openOrders.Count; j++)
                {
                    OrderStateType state = openOrders[j].State;

                    if (state == OrderStateType.Active || state == OrderStateType.Partial)
                    {
                        result.Add(openOrders[j]);
                        continue;
                    }

                    if ((state == OrderStateType.Pending || state == OrderStateType.None) &&
                        positions[i].State == PositionStateType.Opening)
                    {
                        result.Add(openOrders[j]);
                    }
                }
            }

            return result;
        }

        private decimal RoundVolume(decimal volume)
        {
            decimal step = _tab.Security.VolumeStep;

            if (step <= 0)
            {
                return 0;
            }

            decimal rounded = RoundVolumeDown(volume);

            if (rounded <= 0)
            {
                return step;
            }

            return rounded;
        }

        private decimal RoundVolumeDown(decimal volume)
        {
            decimal step = _tab.Security.VolumeStep;

            if (step <= 0)
            {
                return 0;
            }

            decimal steps = Math.Floor(volume / step);
            decimal result = steps * step;

            return Math.Round(result, _tab.Security.DecimalsVolume);
        }

        private decimal RoundPrice(decimal price, Side side)
        {
            return _tab.RoundPrice(price, _tab.Security, side);
        }

        private bool VolumesMatch(decimal planVolume, decimal realVolume, Side side)
        {
            decimal step = _tab.Security.VolumeStep;

            if (step <= 0)
            {
                step = 0.0000001m;
            }

            if (side == Side.Sell)
            {
                if (planVolume > realVolume)
                {
                    return false;
                }

                decimal shrinkTolerance = Math.Max(step, realVolume * 0.1m);

                return planVolume >= realVolume - shrinkTolerance;
            }

            decimal tolerance = Math.Max(step, planVolume * 0.2m);

            return realVolume >= planVolume - tolerance;
        }

        private bool OrdersMatchPlan(List<Order> orders, List<PlanOrder> plan)
        {
            if (orders == null || plan == null || orders.Count != plan.Count)
            {
                return false;
            }

            bool[] used = new bool[plan.Count];

            for (int i = 0; i < orders.Count; i++)
            {
                Order order = orders[i];
                decimal restVolume = order.Volume - order.VolumeExecute;

                bool found = false;

                for (int j = 0; j < plan.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    if (plan[j].Side == order.Side &&
                        plan[j].Price == order.Price &&
                        VolumesMatch(plan[j].Volume, restVolume, order.Side))
                    {
                        used[j] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
