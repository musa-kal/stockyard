"""Write two days of sample exports in the same layout the Exporter produces.

    orders/<yyyy>/<MM>/<dd>/order_lines.parquet
    movements/<yyyy>/<MM>/<dd>/stock_movements.parquet

The column names and types follow the Exporter's Parquet schema, so the dbt
project can be built and tested without running the services or touching S3.
Timestamps are written as UTC-adjusted milliseconds.

Usage (from the repository root):

    pipeline/dbt/.venv/Scripts/python pipeline/dbt/scripts/make_sample_parquet.py

The output goes to pipeline/dbt/sample_data unless --out says otherwise.
"""

from __future__ import annotations

import argparse
import uuid
from datetime import date, datetime, time, timedelta, timezone
from pathlib import Path

import pyarrow as pa
import pyarrow.parquet as pq

DEFAULT_OUT = Path(__file__).resolve().parent.parent / "sample_data"
NAMESPACE = uuid.UUID("6f1c2a7e-3b9d-4c55-9a43-1d2f0e8b7c61")

ORDER_LINES_SCHEMA = pa.schema(
    [
        ("order_id", pa.string()),
        ("order_number", pa.string()),
        ("supplier_code", pa.string()),
        ("job_site_code", pa.string()),
        ("posted_at", pa.timestamp("ms", tz="UTC")),
        ("line_number", pa.int32()),
        ("item_sku", pa.string()),
        ("quantity_ordered", pa.float64()),
        ("quantity_received", pa.float64()),
        ("unit_cost", pa.float64()),
        ("line_total", pa.float64()),
    ]
)

STOCK_MOVEMENTS_SCHEMA = pa.schema(
    [
        ("movement_id", pa.int64()),
        ("item_sku", pa.string()),
        ("job_site_code", pa.string()),
        ("kind", pa.string()),
        ("quantity", pa.float64()),
        ("reference", pa.string()),
        ("occurred_at", pa.timestamp("ms", tz="UTC")),
    ]
)

# Purchase orders: number, supplier, job site, posted on day index, lines of
# (sku, quantity ordered, unit cost).
ORDERS = [
    ("PO-001000", "NORTHGATE", "RIVERSIDE", 0, [("CU-PIPE-15", 120, 4.85), ("CU-ELBOW-15", 60, 1.20)]),
    ("PO-001001", "HARBOURLINE", "WAREHOUSE", 0, [("BV-BRASS-20", 24, 18.40), ("GV-GATE-50", 6, 96.00)]),
    ("PO-001002", "KEELCO", "HILLCREST", 0, [("CU-ELBOW-15", 100, 1.15), ("PEX-PIPE-20", 200, 2.10)]),
    ("PO-001003", "NORTHGATE", "PORTVIEW", 1, [("PEX-PIPE-20", 150, 2.05), ("CU-PIPE-15", 80, 4.90)]),
]

# Movements per day: (hour, minute, sku, job site, kind, signed quantity, reference).
MOVEMENTS = {
    0: [
        (14, 5, "CU-PIPE-15", "RIVERSIDE", "Receipt", 120, "PO-001000"),
        (14, 5, "CU-ELBOW-15", "RIVERSIDE", "Receipt", 60, "PO-001000"),
        (15, 40, "BV-BRASS-20", "WAREHOUSE", "Receipt", 12, "PO-001001"),
        (17, 20, "CU-PIPE-15", "RIVERSIDE", "Issue", -40, "WO-RIV-0142"),
        (17, 25, "CU-ELBOW-15", "RIVERSIDE", "Issue", -18, "WO-RIV-0142"),
    ],
    1: [
        (13, 10, "BV-BRASS-20", "WAREHOUSE", "Receipt", 12, "PO-001001"),
        (13, 10, "GV-GATE-50", "WAREHOUSE", "Receipt", 6, "PO-001001"),
        (14, 45, "CU-ELBOW-15", "HILLCREST", "Receipt", 100, "PO-001002"),
        (14, 45, "PEX-PIPE-20", "HILLCREST", "Receipt", 120, "PO-001002"),
        (16, 0, "PEX-PIPE-20", "HILLCREST", "Issue", -75, "WO-HIL-0031"),
        (16, 30, "BV-BRASS-20", "WAREHOUSE", "Issue", -4, "WO-RIV-0150"),
        (18, 15, "CU-PIPE-15", "RIVERSIDE", "Issue", -50, "WO-RIV-0150"),
        (19, 0, "CU-ELBOW-15", "RIVERSIDE", "Adjustment", -2, "COUNT-0915"),
        (20, 30, "CU-ELBOW-15", "HILLCREST", "ReturnToSupplier", -5, "PO-001002"),
    ],
}


def at(day: date, hour: int, minute: int) -> datetime:
    return datetime.combine(day, time(hour, minute), tzinfo=timezone.utc)


def received_by_end_of(day_index: int) -> dict[tuple[str, str], float]:
    """Quantity received per (order number, sku) up to and including a day."""
    totals: dict[tuple[str, str], float] = {}
    for index in range(day_index + 1):
        for _, _, sku, _, kind, quantity, reference in MOVEMENTS.get(index, []):
            if kind == "Receipt":
                key = (reference, sku)
                totals[key] = totals.get(key, 0.0) + quantity
    return totals


def touched_on(day_index: int) -> set[str]:
    """Orders posted on the day or with a receipt on the day."""
    numbers = {number for number, _, _, posted, _ in ORDERS if posted == day_index}
    numbers |= {m[6] for m in MOVEMENTS.get(day_index, []) if m[4] == "Receipt"}
    return numbers


def order_lines(start: date, day_index: int) -> pa.Table:
    received = received_by_end_of(day_index)
    exported = touched_on(day_index)
    rows = []
    for number, supplier, site, posted, lines in ORDERS:
        if number not in exported:
            continue
        order_id = str(uuid.uuid5(NAMESPACE, number))
        posted_at = at(start + timedelta(days=posted), 9, 30)
        for line_number, (sku, quantity, cost) in enumerate(lines, start=1):
            rows.append(
                {
                    "order_id": order_id,
                    "order_number": number,
                    "supplier_code": supplier,
                    "job_site_code": site,
                    "posted_at": posted_at,
                    "line_number": line_number,
                    "item_sku": sku,
                    "quantity_ordered": float(quantity),
                    "quantity_received": received.get((number, sku), 0.0),
                    "unit_cost": cost,
                    "line_total": round(quantity * cost, 4),
                }
            )
    return pa.Table.from_pylist(rows, schema=ORDER_LINES_SCHEMA)


def stock_movements(start: date, day_index: int, first_id: int) -> pa.Table:
    day = start + timedelta(days=day_index)
    rows = [
        {
            "movement_id": first_id + offset,
            "item_sku": sku,
            "job_site_code": site,
            "kind": kind,
            "quantity": float(quantity),
            "reference": reference,
            "occurred_at": at(day, hour, minute),
        }
        for offset, (hour, minute, sku, site, kind, quantity, reference) in enumerate(MOVEMENTS[day_index])
    ]
    return pa.Table.from_pylist(rows, schema=STOCK_MOVEMENTS_SCHEMA)


def write(table: pa.Table, out: Path, folder: str, day: date, name: str) -> Path:
    target = out / folder / f"{day:%Y}" / f"{day:%m}" / f"{day:%d}" / f"{name}.parquet"
    target.parent.mkdir(parents=True, exist_ok=True)
    pq.write_table(table, target)
    return target


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="folder to write the export layout into")
    parser.add_argument("--start", type=date.fromisoformat, default=date(2026, 9, 14), help="first export day (UTC)")
    args = parser.parse_args()

    next_id = 1
    for day_index in sorted(MOVEMENTS):
        day = args.start + timedelta(days=day_index)
        lines = write(order_lines(args.start, day_index), args.out, "orders", day, "order_lines")
        movements_table = stock_movements(args.start, day_index, next_id)
        next_id += movements_table.num_rows
        movements = write(movements_table, args.out, "movements", day, "stock_movements")
        print(f"{day}: {lines.relative_to(args.out).as_posix()}, {movements.relative_to(args.out).as_posix()}")


if __name__ == "__main__":
    main()
