#!/usr/bin/env python3
"""Independent abstract order-state invariant tests, not compiled OsEngine tests."""
from dataclasses import dataclass
import unittest
@dataclass
class Order:
    requested:int
    filled:int=0
    phase:str='submit_unknown'
    def ack(self):
        if self.phase!='submit_unknown':raise ValueError('out of order ack')
        self.phase='active'
    def fill(self,cumulative:int):
        if self.phase not in ('active','cancel_unknown','reconcile'):raise ValueError('illegal state')
        if cumulative<self.filled or cumulative>self.requested:raise ValueError('bad cumulative fill')
        self.filled=cumulative
        if cumulative==self.requested:self.phase='done'
    def cancel(self):
        if self.phase!='active':raise ValueError('already pending or uncertain')
        self.phase='cancel_unknown'
    def reconcile(self,filled:int,is_active:bool):
        if self.phase not in ('reconcile','cancel_unknown','active','submit_unknown'):
            raise ValueError('not active')
        if filled<self.filled or filled>self.requested:raise ValueError('bad broker state')
        self.filled=filled
        self.phase='active' if is_active else 'done'
class Tests(unittest.TestCase):
    def test_partial_fill_while_cancel_pending(self):
        o=Order(10);o.ack();o.fill(3);o.cancel();o.fill(7)
        self.assertEqual((o.filled,o.phase),(7,'cancel_unknown'))
    def test_cancel_ack_after_fill(self):
        o=Order(10);o.ack();o.cancel();o.fill(10);self.assertEqual(o.phase,'done')
    def test_no_duplicated_cumulative_fill(self):
        o=Order(10);o.ack();o.fill(3);o.fill(3);self.assertEqual(o.filled,3)
    def test_reject_fill_regression(self):
        o=Order(10);o.ack();o.fill(5)
        with self.assertRaises(ValueError):o.fill(4)
    def test_reject_overfill(self):
        o=Order(10);o.ack()
        with self.assertRaises(ValueError):o.fill(11)
    def test_reconcile_after_timeout(self):
        o=Order(10);o.reconcile(0,True);self.assertEqual(o.phase,'active')
    def test_reconcile_rejects_rollback(self):
        o=Order(10);o.ack();o.fill(5);o.cancel()
        with self.assertRaises(ValueError):o.reconcile(4,True)
    def test_no_second_cancel_before_ack(self):
        o=Order(10);o.ack();o.cancel()
        with self.assertRaises(ValueError):o.cancel()
if __name__=='__main__':unittest.main(verbosity=2)
