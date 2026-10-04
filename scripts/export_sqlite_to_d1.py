import argparse
import sqlite3
from pathlib import Path


def sql_value(value):
    if value is None:
        return "NULL"
    if isinstance(value, bytes):
        return "'" + value.decode("utf-8", errors="replace").replace("'", "''") + "'"
    if isinstance(value, (int, float)):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def insert(table, columns, values, conflict_sql=""):
    names = ", ".join(columns)
    data = ", ".join(sql_value(value) for value in values)
    return f"INSERT INTO {table} ({names}) VALUES ({data}) {conflict_sql};"


def export_database(source, output):
    connection = sqlite3.connect(source)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    statements = ["PRAGMA foreign_keys = ON;", "BEGIN IMMEDIATE;"]

    branches = connection.execute("SELECT Id, Name, Location FROM Branches ORDER BY Id").fetchall()
    for row in branches:
        statements.append(insert("branches", ["id", "name", "location"], [row["Id"], row["Name"], row["Location"]],
            "ON CONFLICT(id) DO UPDATE SET name=excluded.name, location=excluded.location"))

    services = connection.execute("SELECT Id, Name, Description, ServiceCode, BranchId FROM Services ORDER BY Id").fetchall()
    for row in services:
        statements.append(insert("services", ["id", "name", "description", "service_code", "branch_id"],
            [row["Id"], row["Name"], row["Description"], row["ServiceCode"], row["BranchId"]],
            "ON CONFLICT(id) DO UPDATE SET name=excluded.name, description=excluded.description, service_code=excluded.service_code, branch_id=excluded.branch_id"))

    roles = {row["Id"]: row["Name"] for row in connection.execute("SELECT Id, Name FROM AspNetRoles")}
    users = connection.execute("SELECT Id, Email, NormalizedEmail, PasswordHash FROM AspNetUsers ORDER BY Id").fetchall()
    for row in users:
        email = row["Email"] or row["Id"]
        normalized = row["NormalizedEmail"] or email.upper()
        statements.append(insert("users", ["id", "email", "normalized_email", "password_hash"],
            [row["Id"], email, normalized, row["PasswordHash"] or ""],
            "ON CONFLICT(id) DO UPDATE SET email=excluded.email, normalized_email=excluded.normalized_email, password_hash=excluded.password_hash"))

    for row in connection.execute("SELECT UserId, RoleId FROM AspNetUserRoles"):
        role_name = roles.get(row["RoleId"])
        if role_name:
            statements.append(
                "INSERT OR IGNORE INTO user_roles (user_id, role_id) "
                f"SELECT {sql_value(row['UserId'])}, id FROM roles WHERE name = {sql_value(role_name)};"
            )

    appointments = connection.execute(
        "SELECT Id, CustomerName, CustomerEmail, CustomerPhone, ServiceId, BranchId, AppointmentDate, TimeSlot, QueueNumber, QueueCode, Status, CreatedAt, CalledAt, ServedAt FROM Appointments ORDER BY Id"
    ).fetchall()
    for row in appointments:
        columns = ["id", "customer_name", "customer_email", "customer_phone", "service_id", "branch_id", "appointment_date", "time_slot", "queue_number", "queue_code", "status", "created_at", "called_at", "served_at"]
        values = [row[column] for column in ["Id", "CustomerName", "CustomerEmail", "CustomerPhone", "ServiceId", "BranchId", "AppointmentDate", "TimeSlot", "QueueNumber", "QueueCode", "Status", "CreatedAt", "CalledAt", "ServedAt"]]
        statements.append(insert("tickets", columns, values, "ON CONFLICT(id) DO NOTHING"))

    statements.append("COMMIT;")
    Path(output).write_text("\n".join(statements) + "\n", encoding="utf-8")
    connection.close()
    print(f"Exported {len(branches)} branches, {len(services)} services, {len(users)} users and {len(appointments)} tickets to {output}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", help="Path to the exported QueueManagement.db file")
    parser.add_argument("output", help="Path for the generated one-time SQL import file")
    args = parser.parse_args()
    export_database(args.source, args.output)
