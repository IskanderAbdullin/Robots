"""Event-driven synthetic order-book order-state oracle. Research only, not OS Engine."""
from dataclasses import dataclass,field
from enum import Enum
from collections import defaultdict

class Phase(str,Enum):
    SUBMITTING="submitting";ACTIVE="active";CANCEL_PENDING="cancel_pending";DONE="done"

@dataclass(frozen=True)
class BookEvent:
    at:int;symbol:str;bid:int;ask:int;bid_qty:int;ask_qty:int
    sell_at_bid:int=0;halted:bool=False;stale:bool=False
    def __post_init__(self):
        if self.at<0 or self.bid<=0 or self.ask<=self.bid or min(self.bid_qty,self.ask_qty,self.sell_at_bid)<0:
            raise ValueError("invalid book")

@dataclass
class Order:
    id:str;symbol:str;price:int;requested:int;placed_at:int;ack_at:int;expiry_at:int;queue_ahead:int
    phase:Phase=Phase.SUBMITTING;filled:int=0;cancel_ack_at:int|None=None
    @property
    def remaining(self):return self.requested-self.filled

@dataclass
class Position:
    units:int=0;total_cost:int=0;entered_at:int=0

@dataclass(frozen=True)
class Policy:
    max_budget:int=900000;max_symbol_notional:int=120000;max_positions:int=8;max_order_qty:int=8
    entry_ttl:int=10;submit_latency:int=2;cancel_latency:int=4;fee_bps:float=8
    stop_ticks:int=75;take_ticks:int=30;time_stop:int=55

@dataclass
class Stats:
    realized_pnl:float=0.0;entry_fills:int=0;partial_fills:int=0;canceled:int=0;rejected:int=0
    stop_exits:int=0;profit_exits:int=0;time_exits:int=0;max_committed:int=0;max_positions:int=0

class Engine:
    def __init__(self,p:Policy):
        if min(p.max_budget,p.max_symbol_notional,p.max_positions,p.max_order_qty)<=0:raise ValueError("bad budget")
        self.p=p;self.orders={};self.active_ids=set();self.positions=defaultdict(Position)
        self.books={};self.stats=Stats();self.time=0;self.sequence=0
    def committed(self):
        return sum(pos.total_cost for pos in self.positions.values())+sum(
            self.orders[k].price*self.orders[k].remaining for k in self.active_ids)
    def invariant(self):
        assert self.committed()<=self.p.max_budget,"global budget exceeded"
        assert all(self.orders[k].remaining>=0 and self.orders[k].queue_ahead>=0 for k in self.active_ids)
        assert all(p.units>=0 and p.total_cost>=0 for p in self.positions.values())
        active=[self.orders[k].symbol for k in self.active_ids]
        assert len(active)==len(set(active)),"duplicate entry order"
        self.stats.max_committed=max(self.stats.max_committed,self.committed())
        n=sum(p.units>0 for p in self.positions.values())
        self.stats.max_positions=max(self.stats.max_positions,n)
        assert n<=self.p.max_positions
    def place(self,symbol,price,qty,now,queue_ahead=0):
        self.clock_advance(now)
        if price<=0 or qty<=0 or qty>self.p.max_order_qty or queue_ahead<0 or symbol in (
            self.orders[k].symbol for k in self.active_ids):
            self.stats.rejected+=1;return None
        pos=self.positions[symbol]
        slots={s for s,p in self.positions.items() if p.units>0}
        slots.update(self.orders[k].symbol for k in self.active_ids)
        if pos.total_cost+qty*price>self.p.max_symbol_notional or self.committed()+qty*price>self.p.max_budget or (
            symbol not in slots and len(slots)>=self.p.max_positions):
            self.stats.rejected+=1;return None
        self.sequence+=1;ident=f"O{self.sequence}"
        self.orders[ident]=Order(ident,symbol,price,qty,now,now+self.p.submit_latency,
                                 now+self.p.entry_ttl,queue_ahead)
        self.active_ids.add(ident);self.invariant()
        return ident
    def cancel(self,oid,now):
        self.clock_advance(now);o=self.orders[oid]
        if o.phase not in (Phase.SUBMITTING,Phase.ACTIVE):return False
        o.phase=Phase.CANCEL_PENDING;o.cancel_ack_at=now+self.p.cancel_latency
        return True
    def clock_advance(self,now):
        if now<self.time:raise ValueError("backward clock")
        self.time=now
        for k in tuple(self.active_ids):
            o=self.orders[k]
            if o.phase==Phase.SUBMITTING and now>=o.ack_at:o.phase=Phase.ACTIVE
            if o.phase==Phase.CANCEL_PENDING and o.cancel_ack_at is not None and now>=o.cancel_ack_at:
                o.phase=Phase.DONE;self.stats.canceled+=1
            if o.phase in (Phase.SUBMITTING,Phase.ACTIVE) and now>=o.expiry_at:
                o.phase=Phase.CANCEL_PENDING;o.cancel_ack_at=now+self.p.cancel_latency
            if o.phase==Phase.DONE:self.active_ids.discard(k)
        self.invariant()
    def event(self,e:BookEvent):
        self.clock_advance(e.at);self.books[e.symbol]=e
        if e.halted or e.stale:
            for k in tuple(self.active_ids):
                if self.orders[k].symbol==e.symbol:self.cancel(k,e.at)
            return
        pos=self.positions[e.symbol]
        if pos.units:
            avg=pos.total_cost/pos.units
            cause=("stop" if e.bid<=avg-self.p.stop_ticks else
                   "take" if e.bid>=avg+self.p.take_ticks else
                   "time" if e.at-pos.entered_at>=self.p.time_stop else None)
            if cause:self.exit(e.symbol,e.bid,e.at,cause)
        for k in tuple(self.active_ids):
            o=self.orders[k]
            if o.symbol!=e.symbol or o.phase not in (Phase.ACTIVE,Phase.CANCEL_PENDING) or e.at<=o.placed_at:continue
            if e.ask<=o.price:executable=o.remaining
            elif e.bid==o.price and e.sell_at_bid>0:
                consumed=min(e.sell_at_bid,o.queue_ahead);o.queue_ahead-=consumed
                executable=min(o.remaining,e.sell_at_bid-consumed)
            else:executable=0
            if executable:
                pos=self.positions[e.symbol]
                if pos.units==0:pos.entered_at=e.at
                pos.units+=executable;pos.total_cost+=o.price*executable;o.filled+=executable
                self.stats.entry_fills+=executable
                if executable<o.requested:self.stats.partial_fills+=1
                if o.remaining==0:o.phase=Phase.DONE;self.active_ids.discard(k)
        self.invariant()
    def exit(self,symbol,bid,now,cause):
        pos=self.positions[symbol]
        if not pos.units:return
        for k in tuple(self.active_ids):
            o=self.orders[k]
            if o.symbol==symbol and o.phase in (Phase.SUBMITTING,Phase.ACTIVE):self.cancel(k,now)
            if o.symbol==symbol and o.phase==Phase.CANCEL_PENDING:return
        sale=bid*pos.units;purchase=pos.total_cost
        self.stats.realized_pnl+=sale-purchase-(purchase+sale)*self.p.fee_bps/20000
        if cause=="stop":self.stats.stop_exits+=1
        elif cause=="take":self.stats.profit_exits+=1
        else:self.stats.time_exits+=1
        self.positions[symbol]=Position();self.invariant()
    def finish(self,now):
        self.clock_advance(now)
        for k in tuple(self.active_ids):
            self.orders[k].phase=Phase.DONE;self.active_ids.discard(k);self.stats.canceled+=1
        for symbol,pos in list(self.positions.items()):
            if pos.units and symbol in self.books:self.exit(symbol,self.books[symbol].bid,now,"time")
        self.invariant()
