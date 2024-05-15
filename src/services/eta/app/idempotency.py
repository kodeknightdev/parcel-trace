from __future__ import annotations

import threading
from collections import OrderedDict


# no database here, so remember recent event ids in memory (lost on restart, which is fine)
class ProcessedEvents:
    def __init__(self, capacity: int = 50_000):
        self.capacity = capacity
        self._seen: OrderedDict[str, None] = OrderedDict()
        self._lock = threading.Lock()

    def seen(self, event_id: str) -> bool:
        with self._lock:
            if event_id in self._seen:
                self._seen.move_to_end(event_id)
                return True
            return False

    def mark(self, event_id: str) -> None:
        with self._lock:
            self._seen[event_id] = None
            self._seen.move_to_end(event_id)
            while len(self._seen) > self.capacity:
                self._seen.popitem(last=False)

    def __len__(self) -> int:
        return len(self._seen)
