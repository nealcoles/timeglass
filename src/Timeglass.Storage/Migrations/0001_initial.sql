-- Initial schema. Timestamps are Unix milliseconds, UTC.

CREATE TABLE apps (
    id           INTEGER PRIMARY KEY,
    key          TEXT    NOT NULL UNIQUE,
    display_name TEXT    NOT NULL
);

CREATE TABLE intervals (
    id           TEXT    PRIMARY KEY,                 -- engine Guid, "D" format
    start_utc    INTEGER NOT NULL,
    end_utc      INTEGER NOT NULL,
    kind         INTEGER NOT NULL,                    -- 0 = active, 1 = away
    app_id       INTEGER NULL REFERENCES apps (id),
    exe_path     TEXT    NULL,
    window_title TEXT    NULL,
    is_open      INTEGER NOT NULL DEFAULT 0,
    CHECK (end_utc >= start_utc),
    CHECK (kind IN (0, 1)),
    CHECK (is_open IN (0, 1))
);

CREATE INDEX ix_intervals_start_utc ON intervals (start_utc);
