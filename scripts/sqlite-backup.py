#!/usr/bin/env python3
"""Consistent online backup and offline restore of the forum SQLite database."""
import argparse
import os
from pathlib import Path
import sqlite3
import tempfile


def checked_connection(path):
    connection = sqlite3.connect(f"file:{path.resolve()}?mode=ro", uri=True, timeout=30)
    if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        connection.close()
        raise RuntimeError("Database integrity check failed")
    if connection.execute("PRAGMA foreign_key_check").fetchall():
        connection.close()
        raise RuntimeError("Database foreign-key check failed")
    return connection


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["backup", "restore"])
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--service-stopped", action="store_true", help="Required for restore; stop the application first")
    args = parser.parse_args()
    if args.action == "restore" and not args.service_stopped:
        parser.error("Restore requires the stopped application and --service-stopped")
    if args.destination.exists():
        parser.error("Destination already exists. Restore to a new path or move the previous stopped database aside first.")
    args.destination.parent.mkdir(parents=True, exist_ok=True)
    # Never copy a live SQLite main file alone: the backup API includes committed WAL data.
    descriptor, temporary = tempfile.mkstemp(prefix="forum-backup-", dir=args.destination.parent)
    os.close(descriptor)
    try:
        with checked_connection(args.source) as source, sqlite3.connect(temporary) as target:
            source.backup(target)
        with checked_connection(Path(temporary)):
            pass
        os.chmod(temporary, 0o600)
        os.replace(temporary, args.destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    print(f"Verified {args.action}: {args.destination}")


if __name__ == "__main__":
    main()
