"""Regression suite for standalone reference oracle (not compiled OS Engine)."""
import random,unittest
from engine import Engine,Policy,BookEvent,Phase

def quote(t,s="A",bid=100,ask=110,sell=0,halted=False,stale=False):
    return BookEvent(t,s,bid,ask,500,500,sell,halted,stale)

class TestLifecycle(unittest.TestCase):
    def test_no_same_event_fill(self):
        e=Engine(Policy(submit_latency=0));k=e.place("A",100,3,0)
        e.event(quote(0,bid=99,ask=100));self.assertEqual(e.orders[k].filled,0)
        e.event(quote(1,bid=99,ask=100));self.assertEqual(e.orders[k].filled,3)
    def test_queue_and_partial(self):
        e=Engine(Policy(submit_latency=0));k=e.place("A",100,5,0,8)
        e.event(quote(1,sell=10));self.assertEqual((e.orders[k].filled,e.orders[k].queue_ahead),(2,0))
        e.event(quote(2,sell=2));self.assertEqual(e.orders[k].filled,4)
    def test_fill_during_cancel(self):
        e=Engine(Policy(submit_latency=0,cancel_latency=4));k=e.place("A",100,5,0)
        e.cancel(k,1);e.event(quote(2,bid=99,ask=100));self.assertEqual(e.orders[k].filled,5)
    def test_ack_prevents_late_fill(self):
        e=Engine(Policy(submit_latency=0,cancel_latency=2));k=e.place("A",100,5,0)
        e.cancel(k,1);e.event(quote(4,bid=99,ask=100));self.assertEqual(e.orders[k].filled,0)
    def test_partial_preserves_reserve(self):
        e=Engine(Policy(max_budget=1200,max_symbol_notional=1200,submit_latency=0))
        k=e.place("A",100,8,0);e.event(quote(1,sell=3))
        self.assertEqual(e.committed(),800)
        e.cancel(k,2);e.clock_advance(6);self.assertEqual(e.committed(),300)
    def test_budget_cap(self):
        e=Engine(Policy(max_budget=1000,max_symbol_notional=1000))
        self.assertIsNotNone(e.place("A",100,8,0));self.assertIsNone(e.place("B",100,4,0))
    def test_slot_reservation(self):
        e=Engine(Policy(max_positions=1,max_budget=10000,max_symbol_notional=1000))
        self.assertIsNotNone(e.place("A",100,2,0));self.assertIsNone(e.place("B",100,2,0))
    def test_slot_release(self):
        e=Engine(Policy(max_positions=1,max_budget=10000,max_symbol_notional=1000,cancel_latency=2))
        k=e.place("A",100,2,0);e.cancel(k,1)
        self.assertIsNone(e.place("B",100,2,1));self.assertIsNotNone(e.place("B",100,2,3))
    def test_no_repeated_entry(self):
        e=Engine(Policy());self.assertIsNotNone(e.place("A",100,2,0));self.assertIsNone(e.place("A",100,2,0))
    def test_stop_exit(self):
        e=Engine(Policy(submit_latency=0,stop_ticks=3));e.place("A",100,2,0)
        e.event(quote(1,sell=2));e.event(quote(2,bid=90,ask=104));self.assertEqual(e.stats.stop_exits,1)
    def test_halt_cancels(self):
        e=Engine(Policy(submit_latency=0));k=e.place("A",100,2,0)
        e.event(quote(1,halted=True));self.assertEqual(e.orders[k].phase,Phase.CANCEL_PENDING)
    def test_stale_cancels(self):
        e=Engine(Policy(submit_latency=0));k=e.place("A",100,2,0)
        e.event(quote(1,stale=True));self.assertEqual(e.orders[k].phase,Phase.CANCEL_PENDING)
    def test_expiry_latency(self):
        e=Engine(Policy(submit_latency=0,entry_ttl=2,cancel_latency=3));k=e.place("A",100,2,0)
        e.clock_advance(2);self.assertEqual(e.orders[k].phase,Phase.CANCEL_PENDING)
        e.clock_advance(5);self.assertEqual(e.orders[k].phase,Phase.DONE)
    def test_reject_backward_clock(self):
        e=Engine(Policy());e.clock_advance(10)
        with self.assertRaises(ValueError):e.clock_advance(9)
    def test_no_crossed_quote(self):
        with self.assertRaises(ValueError):quote(0,bid=111,ask=110)
    def test_logical_clock_universe_invariant(self):
        def simulate(extra):
            e=Engine(Policy(submit_latency=1,entry_ttl=5,cancel_latency=2))
            e.place("A",100,2,0)
            for t in range(1,4):
                for s in ["A"]+[f"B{x}" for x in range(extra)]:
                    e.event(quote(t,s,99 if s=="A" else 100,100 if s=="A" else 110))
            return e.orders["O1"].filled
        self.assertEqual(simulate(0),simulate(24))
    def test_finish_flattens(self):
        e=Engine(Policy(submit_latency=0));e.place("A",100,2,0)
        e.event(quote(1,sell=2));e.finish(2)
        self.assertEqual(e.positions["A"].units,0)
    def test_fee(self):
        e=Engine(Policy(submit_latency=0,fee_bps=10,take_ticks=5));e.place("A",100,1,0)
        e.event(quote(1,sell=1));e.event(quote(2,bid=105,ask=110))
        self.assertAlmostEqual(e.stats.realized_pnl,5-(100+105)*10/20000)
    def test_random_200(self):
        for seed in range(200):
            r=random.Random(seed);e=Engine(Policy(max_budget=5000,max_symbol_notional=2000,
                        max_positions=3,submit_latency=r.randrange(4),cancel_latency=r.randrange(1,5)))
            for t in range(60):
                s="ABCD"[r.randrange(4)];bid=r.randrange(70,101)
                e.event(quote(t,s,bid,bid+r.randrange(1,15),r.randrange(12)))
                if r.random()<.4:e.place(s,bid,r.randrange(1,8),t,r.randrange(20))
                if r.random()<.08 and e.active_ids:e.cancel(r.choice(tuple(sorted(e.active_ids))),t)
                e.invariant()
            e.finish(65);self.assertEqual(e.committed(),0)
if __name__=="__main__":unittest.main(verbosity=2)
