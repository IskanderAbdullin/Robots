

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

        

        
        private StrategyParameterString _regime;

        
        
        private StrategyParameterString _buyMode;

        
        
        
        private StrategyParameterDecimal _buyTotalVolume;

        
        
        
        
        private StrategyParameterString _buyVolumeMode;

        
        
        private StrategyParameterDecimal _levelVolumePercent;

        
        
        private StrategyParameterString _sellVolumeMode;

        
        private StrategyParameterDecimal _sellPercent;

        public MarketMakerStakan3(string name, StartProgram startProgram) : base(name, startProgram)
        {
            
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            
            
            
            
            
            
            
            
            
            _tab.ManualPositionSupport.SecondToOpenIsOn = false;   
            _tab.ManualPositionSupport.SecondToCloseIsOn = false;  
            _tab.ManualPositionSupport.SetbackToOpenIsOn = false;  
            _tab.ManualPositionSupport.SetbackToCloseIsOn = false; 
            _tab.ManualPositionSupport.StopIsOn = false;           
            _tab.ManualPositionSupport.ProfitIsOn = false;         
            _tab.ManualPositionSupport.DoubleExitIsOn = false;     
            _tab.ManualPositionSupport.OrderTypeTime = OrderTypeTime.GTC; 
            _tab.ManualPositionSupport.Save();                     

            
            _regime = CreateParameter("Режим", "Off", new[] { "Off", "On" });
            _buyMode = CreateParameter("Режим покупки", "Распределение сверху вниз", new[] { "Один уровень (макс. объём)", "Распределение сверху вниз" });
            
            
            
            
            
            
            _buyTotalVolume = CreateParameter("Суммарный объём покупки (лоты или % портфеля)", 100.0m, 0.0001m, 100000.0m, 0.0001m);
            _buyVolumeMode = CreateParameter("Режим объёма покупки", "% от стоимости портфеля", new[] { "Фиксированный", "% от стоимости портфеля", "Максимум без маржи" });
            _levelVolumePercent = CreateParameter("Процент от объёма уровня", 100.0m, 1.0m, 1000.0m, 1.0m);
            _sellVolumeMode = CreateParameter("Режим объёма продажи", "% от бумаг в портфеле", new[] { "% от бумаг в портфеле", "% от всего портфеля" });
            _sellPercent = CreateParameter("Процент продажи", 100.0m, 1.0m, 100.0m, 1.0m);

            
            _tab.MarketDepthUpdateEvent += MarketDepthUpdateHandler;

            
            
            _tab.OrderUpdateEvent += OrderUpdateHandler;
        }

        
        public override string GetNameStrategyType()
        {
            return "MarketMakerStakan3";
        }

        
        public override void ShowIndividualSettingsDialog()
        {
        }

        

        
        private class PlanOrder
        {
            public Side Side;      
            public decimal Price;  
            public decimal Volume; 
        }

        
        private bool _ordersClearedInOffMode = false;

        
        
        
        private readonly List<int> _ordersOnCancel = new List<int>();

        
        private readonly object _planLock = new object();

        
        private void MarketDepthUpdateHandler(MarketDepth marketDepth)
        {
            try
            {
                lock (_planLock)
                {
                    
                    if (_regime.ValueString == "Off")
                    {
                        if (!_ordersClearedInOffMode)
                        {
                            CancelAllActiveOrders();
                            _ordersClearedInOffMode = true;
                        }
                        return;
                    }

                    _ordersClearedInOffMode = false;

                    
                    if (marketDepth == null ||
                        marketDepth.Asks == null || marketDepth.Asks.Count == 0 ||
                        marketDepth.Bids == null || marketDepth.Bids.Count == 0)
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

                    
                    List<PlanOrder> newPlan = BuildPlan(marketDepth);

                    
                    
                    
                    
                    
                    List<Order> activeOrders = GetMyActiveOrders();

                    
                    
                    
                    if (OrdersMatchPlan(activeOrders, newPlan))
                    {
                        return;
                    }

                    
                    ApplyPlan(newPlan, activeOrders);
                }
            }
            catch (Exception e)
            {
                SendNewLogMessage("Ошибка в обработке стакана: " + e, LogMessageType.Error);
            }
        }

        
        
        
        
        
        private void OrderUpdateHandler(Order order)
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

                
                
                if (_ordersOnCancel.Contains(order.NumberUser))
                {
                    return;
                }

                
                _ordersOnCancel.Add(order.NumberUser);

                _tab.CloseOrder(order);
            }
        }

        

        
        private List<PlanOrder> BuildPlan(MarketDepth marketDepth)
        {
            List<PlanOrder> plan = new List<PlanOrder>();

            
            BuildBuyPlan(marketDepth, plan);

            
            BuildSellPlan(marketDepth, plan);

            return plan;
        }

        
        private void BuildBuyPlan(MarketDepth marketDepth, List<PlanOrder> plan)
        {
            
            
            decimal totalBuyVolume = GetBuyTotalVolume(marketDepth);

            if (totalBuyVolume <= 0)
            {
                return; 
            }

            
            
            List<decimal> foreignVolumes = GetForeignVolumes(marketDepth.Bids, Side.Buy);

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

                decimal price = RoundPrice(Convert.ToDecimal(marketDepth.Bids[bestIndex].Price), Side.Buy);
                decimal volume = RoundVolume(totalBuyVolume);

                if (price > 0 && volume > 0)
                {
                    plan.Add(new PlanOrder { Side = Side.Buy, Price = price, Volume = volume });
                }
            }
            else
            {
                
                
                
                decimal rest = totalBuyVolume;

                for (int i = 0; i < marketDepth.Bids.Count && rest > 0; i++)
                {
                    
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

                    decimal price = RoundPrice(Convert.ToDecimal(marketDepth.Bids[i].Price), Side.Buy);

                    if (price > 0)
                    {
                        plan.Add(new PlanOrder { Side = Side.Buy, Price = price, Volume = volume });
                        rest -= volume;
                    }
                }
            }
        }

        
        private void BuildSellPlan(MarketDepth marketDepth, List<PlanOrder> plan)
        {
            
            decimal totalSellVolume = GetSellTotalVolume(marketDepth);

            if (totalSellVolume <= 0)
            {
                return;
            }

            // План описывает итоговое желаемое состояние: весь доступный объём из портфеля.
            // Уже стоящие заявки покрываются сверкой в ApplyPlan, их объём из плана не вычитаем —
            // иначе стоящая заявка каждый тик «не совпадает с планом» и снимается.
            decimal rest = totalSellVolume;

            List<decimal> foreignVolumes = GetForeignVolumes(marketDepth.Asks, Side.Sell);

            for (int i = 0; i < marketDepth.Asks.Count && rest > 0; i++)
            {
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

                decimal price = RoundPrice(Convert.ToDecimal(marketDepth.Asks[i].Price), Side.Sell);

                if (price > 0)
                {
                    plan.Add(new PlanOrder { Side = Side.Sell, Price = price, Volume = volume });
                    rest -= volume;
                }
            }
        }

        
        
        private decimal GetBuyTotalVolume(MarketDepth marketDepth)
        {
            
            if (_buyVolumeMode.ValueString == "Фиксированный")
            {
                return _buyTotalVolume.ValueDecimal;
            }

            Portfolio portfolio = _tab.Portfolio;

            if (portfolio == null)
            {
                SendNewLogMessage("Портфель недоступен — объём покупки не считаем", LogMessageType.Error);
                return 0;
            }

            
            decimal bestBid = Convert.ToDecimal(marketDepth.Bids[0].Price);

            if (bestBid <= 0)
            {
                return 0;
            }

            decimal moneyForBuy;

            if (_buyVolumeMode.ValueString == "% от стоимости портфеля")
            {
                
                
                
                
                
                
                
                
                
                
                
                
                
                moneyForBuy = GetPortfolioValue(portfolio) * _buyTotalVolume.ValueDecimal / 100m;

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

        
        
        
        
        
        
        
        private decimal GetPortfolioValue(Portfolio portfolio)
        {
            return portfolio.ValueCurrent;
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

        
        
        private decimal GetSellTotalVolume(MarketDepth marketDepth)
        {
            Portfolio portfolio = _tab.Portfolio;

            if (portfolio == null)
            {
                SendNewLogMessage("Портфель недоступен — объём продажи не считаем", LogMessageType.Error);
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
                        securityQty = positionsOnBoard[i].ValueCurrent;
                        break;
                    }
                }

                return securityQty * _sellPercent.ValueDecimal / 100m;
            }
            else
            {
                
                
                decimal bestAsk = Convert.ToDecimal(marketDepth.Asks[0].Price);

                if (bestAsk <= 0)
                {
                    return 0;
                }

                decimal moneyForSell = GetPortfolioValue(portfolio) * _sellPercent.ValueDecimal / 100m;

                return moneyForSell / bestAsk;
            }
        }

        
        
        private List<decimal> GetForeignVolumes(List<MarketDepthLevel> levels, Side side)
        {
            List<decimal> result = new List<decimal>();

            for (int i = 0; i < levels.Count; i++)
            {
                
                decimal foreign = side == Side.Buy
                    ? Convert.ToDecimal(levels[i].Bid)
                    : Convert.ToDecimal(levels[i].Ask);

                result.Add(foreign);
            }

            
            List<Order> myActiveOrders = GetMyActiveOrders();

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

        

        
        
        private void ApplyPlan(List<PlanOrder> plan, List<Order> activeOrders)
        {
            
            bool[] planCovered = new bool[plan.Count];

            
            for (int i = 0; i < activeOrders.Count; i++)
            {
                Order order = activeOrders[i];
                decimal restVolume = order.Volume - order.VolumeExecute;

                bool covered = false;

                for (int j = 0; j < plan.Count; j++)
                {
                    if (planCovered[j])
                    {
                        continue;
                    }

                    
                    
                    
                    
                    
                    
                    if (plan[j].Side == order.Side &&
                        plan[j].Price == order.Price &&
                        VolumesMatch(plan[j].Volume, restVolume))
                    {
                        
                        planCovered[j] = true;
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    
                    
                    
                    
                    
                    
                    SendNewLogMessage("Снимаем заявку " + order.Side + " " + order.Price +
                                      " объём " + restVolume + " (не совпала с планом)", LogMessageType.Trade);
                    CancelOrderSafe(order);
                }
            }

            
            for (int j = 0; j < plan.Count; j++)
            {
                if (planCovered[j])
                {
                    continue;
                }

                SendNewLogMessage("Выставляем заявку " + plan[j].Side + " " + plan[j].Price +
                                  " объём " + plan[j].Volume, LogMessageType.Trade);

                if (plan[j].Side == Side.Buy)
                {
                    
                    
                    _tab.BuyAtLimit(plan[j].Volume, plan[j].Price);
                }
                else
                {
                    _tab.SellAtLimit(plan[j].Volume, plan[j].Price);
                }
            }
        }

        
        private void CancelAllActiveOrders()
        {
            List<Order> activeOrders = GetMyActiveOrders();

            for (int i = 0; i < activeOrders.Count; i++)
            {
                SendNewLogMessage("Режим Off: снимаем заявку " + activeOrders[i].Side +
                                  " " + activeOrders[i].Price, LogMessageType.Trade);
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

        
        
        private decimal GetNetPositionVolume()
        {
            decimal net = 0;

            List<Position> positions = _tab.PositionsOpenAll;

            if (positions == null)
            {
                return 0;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].Direction == Side.Buy)
                {
                    net += positions[i].OpenVolume;
                }
                else if (positions[i].Direction == Side.Sell)
                {
                    net -= positions[i].OpenVolume;
                }
            }

            return net;
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

        
        
        
        
        
        
        private bool VolumesMatch(decimal planVolume, decimal realVolume)
        {
            decimal step = _tab.Security.VolumeStep;

            if (step <= 0)
            {
                step = 0.0000001m; 
            }

            decimal tolerance = Math.Max(step / 2m, planVolume * 0.01m);

            return Math.Abs(planVolume - realVolume) <= tolerance;
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
                        VolumesMatch(plan[j].Volume, restVolume))
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
