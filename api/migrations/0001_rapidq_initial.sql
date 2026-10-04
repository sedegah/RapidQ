CREATE TABLE IF NOT EXISTS branches (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL CHECK (length(trim(name)) > 0),
    location TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS services (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL CHECK (length(trim(name)) > 0),
    description TEXT NOT NULL DEFAULT '',
    service_code TEXT NOT NULL CHECK (length(trim(service_code)) > 0),
    branch_id INTEGER NOT NULL,
    FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS tickets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_name TEXT NOT NULL CHECK (length(trim(customer_name)) > 0),
    customer_email TEXT NOT NULL DEFAULT '',
    customer_phone TEXT NOT NULL,
    service_id INTEGER NOT NULL,
    branch_id INTEGER NOT NULL,
    appointment_date TEXT NOT NULL,
    time_slot TEXT NOT NULL,
    queue_number INTEGER NOT NULL UNIQUE CHECK (queue_number > 0),
    queue_code TEXT NOT NULL UNIQUE,
    status INTEGER NOT NULL DEFAULT 0 CHECK (status BETWEEN 0 AND 4),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    called_at TEXT NULL,
    served_at TEXT NULL,
    FOREIGN KEY (service_id) REFERENCES services(id) ON DELETE CASCADE,
    FOREIGN KEY (branch_id) REFERENCES branches(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL,
    normalized_email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS roles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE CHECK (name IN ('Admin', 'Staff', 'Customer'))
);

CREATE TABLE IF NOT EXISTS user_roles (
    user_id TEXT NOT NULL,
    role_id INTEGER NOT NULL,
    PRIMARY KEY (user_id, role_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_tickets_queue_status ON tickets(branch_id, queue_number, status);
CREATE INDEX IF NOT EXISTS idx_tickets_history ON tickets(status, served_at, created_at);
CREATE INDEX IF NOT EXISTS idx_tickets_service ON tickets(service_id);
CREATE INDEX IF NOT EXISTS idx_services_branch ON services(branch_id, name);
CREATE INDEX IF NOT EXISTS idx_user_roles_role ON user_roles(role_id, user_id);

INSERT OR IGNORE INTO roles (name) VALUES ('Admin'), ('Staff'), ('Customer');
INSERT OR IGNORE INTO branches (id, name, location) VALUES (1, 'Main Branch', 'Head Office');
INSERT OR IGNORE INTO services (id, name, description, service_code, branch_id) VALUES
    (1, 'Teller Services', 'Cash withdrawals and deposits', 'TELL', 1),
    (2, 'Account Opening', 'New customer onboarding', 'ACCT', 1),
    (3, 'Loan Support', 'Loan application assistance', 'LOAN', 1),
    (4, 'Card Services', 'Debit and credit card support', 'CARD', 1),
    (5, 'Wealth Advice', 'Investment consultation', 'WEAL', 1),
    (6, 'Customer Support', 'General service enquiries', 'CUST', 1);
