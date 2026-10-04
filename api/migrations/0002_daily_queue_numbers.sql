ALTER TABLE tickets RENAME TO tickets_before_daily_queue;

CREATE TABLE tickets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_name TEXT NOT NULL CHECK (length(trim(customer_name)) > 0),
    customer_email TEXT NOT NULL DEFAULT '',
    customer_phone TEXT NOT NULL,
    service_id INTEGER NOT NULL,
    branch_id INTEGER NOT NULL,
    appointment_date TEXT NOT NULL,
    time_slot TEXT NOT NULL,
    queue_number INTEGER NOT NULL CHECK (queue_number > 0),
    queue_code TEXT NOT NULL,
    status INTEGER NOT NULL DEFAULT 0 CHECK (status BETWEEN 0 AND 4),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    called_at TEXT NULL,
    served_at TEXT NULL,
    FOREIGN KEY (service_id) REFERENCES services(id) ON DELETE CASCADE,
    FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
);

INSERT INTO tickets (
    id, customer_name, customer_email, customer_phone, service_id, branch_id,
    appointment_date, time_slot, queue_number, queue_code, status, created_at,
    called_at, served_at
)
SELECT
    id, customer_name, customer_email, customer_phone, service_id, branch_id,
    appointment_date, time_slot, queue_number, queue_code, status, created_at,
    called_at, served_at
FROM tickets_before_daily_queue;

DROP TABLE tickets_before_daily_queue;

CREATE UNIQUE INDEX idx_tickets_daily_number
    ON tickets(branch_id, substr(appointment_date, 1, 10), queue_number);
CREATE UNIQUE INDEX idx_tickets_daily_code
    ON tickets(branch_id, substr(appointment_date, 1, 10), queue_code);
CREATE INDEX IF NOT EXISTS idx_tickets_queue_status ON tickets(branch_id, queue_number, status);
CREATE INDEX IF NOT EXISTS idx_tickets_history ON tickets(status, served_at, created_at);
CREATE INDEX IF NOT EXISTS idx_tickets_service ON tickets(service_id);
