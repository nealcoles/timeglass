-- What retention keeps of a day once its intervals are deleted: the time per kind and app.
-- A day is a local date (yyyy-MM-dd) in the time zone the PC was in when the day was rolled up.

CREATE TABLE daily_app_totals (
    day         TEXT    NOT NULL,
    kind        INTEGER NOT NULL,                    -- 0 = active, 1 = away
    app_id      INTEGER NULL REFERENCES apps (id),   -- NULL for away time and active time without an app
    duration_ms INTEGER NOT NULL,
    first_utc   INTEGER NOT NULL,                    -- start of the earliest time counted
    last_utc    INTEGER NOT NULL,                    -- end of the latest time counted
    CHECK (kind IN (0, 1)),
    CHECK (duration_ms >= 0),
    CHECK (last_utc >= first_utc)
);

-- One row per day, kind and app; app ids start at 1, so 0 stands for "no app".
CREATE UNIQUE INDEX ux_daily_app_totals ON daily_app_totals (day, kind, COALESCE(app_id, 0));
