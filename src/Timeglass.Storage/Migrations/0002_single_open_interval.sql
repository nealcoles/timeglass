-- At most one interval can be open: the one the tracker is writing right now.
-- Anything still open from before is closed at its last checkpoint first.

UPDATE intervals SET is_open = 0 WHERE is_open = 1;

CREATE UNIQUE INDEX ux_intervals_single_open ON intervals (is_open) WHERE is_open = 1;
