#!/usr/bin/env python3
"""Read-only source inventory. Not a functional test or compilation."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path

MARKERS = {
    "depth_event_subscription": "MarketDepthUpdateEvent",
    "depth_snapshot_copy": "GetCopy()",
    "serialized_event_planning": "_planLock",
    "price_step": "PriceStep",
    "volume_step": "VolumeStep",
    "portfolio_access": "Portfolio",
    "portfolio_positions": "GetPositionOnBoard",
    "order_buy": "BuyAtLimit",
    "wall_buy_mode": "Перед большой заявкой",
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", default="MarketMakerStakan3.cs")
    parser.add_argument("--out", default="osengine-report.json")
    args = parser.parse_args()
    source = Path(args.file).read_bytes()
    decoded = source.decode("utf-8-sig")
    checks = {name: token in decoded for name, token in MARKERS.items()}
    report = {
        "schema": "osengine-lab-v1",
        "bot": "MarketMakerStakan3",
        "repository": os.getenv("GITHUB_REPOSITORY", ""),
        "sha": os.getenv("GITHUB_SHA", ""),
        "timestamp_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "run_url": f"{os.getenv('GITHUB_SERVER_URL', 'https://github.com')}/{os.getenv('GITHUB_REPOSITORY', '')}/actions/runs/{os.getenv('GITHUB_RUN_ID', '')}",
        "result": "source_inventory_only",
        "source_sha256": hashlib.sha256(source).hexdigest(),
        "matched": sum(checks.values()),
        "total": len(checks),
        "markers": checks,
        "offline": True,
        "functional_tests_passed": False,
        "fresh_copy_verified": False,
        "ready_for_live": False,
    }
    Path(args.out).write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    summary_file = os.getenv("GITHUB_STEP_SUMMARY")
    if summary_file:
        summary = (
            "## OsEngine Lab — MarketMakerStakan3\n"
            f"- Commit: {report['sha']}\n"
            f"- Source-only inventory: **{report['matched']}/{report['total']}** markers\n"
            f"- SHA256: {report['source_sha256']}\n"
            "- **Not a compilation or functional test.** No broker or orders.\n"
            "- Custom/Robots fresh-copy launch: **not verified**.\n"
            "- READY requires separately verified startup and offline tests.\n"
        )
        with open(summary_file, "a", encoding="utf-8") as fh:
            fh.write(summary)
    print(f"Inventory: {report['matched']}/{report['total']} textual markers. NOT a test result.")

if __name__ == "__main__":
    main()
