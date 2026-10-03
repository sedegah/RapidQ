# RapidQ Implementation Plan

This document outlines the step-by-step plan to bring the RapidQ project up to the required specifications.

## Phase 1: Database & Authentication Foundation
**Objective**: Secure the application with role-based access control.
1. **Database Schema Updates**:
   - Add Entity Framework Core Identity or custom `User` and `Role` tables to `QueueDbContext`.
   - Update `QueueDbContext` to inherit from `IdentityDbContext` (if using Identity).
   - Create EF Core migrations and apply them to the SQLite database.
2. **API Authentication**:
   - Implement JWT-based authentication in `QueueManagement.Api/Program.cs`.
   - Create `POST /auth/register` and `POST /auth/login` endpoints.
   - Seed default roles (`Admin`, `Staff`, `Customer`) and an initial Admin user on startup.
3. **Client Authorization**:
   - Add a custom `AuthenticationStateProvider` to `QueueManagement.Client` to handle JWT tokens.
   - Create a Login component/page in the Blazor client.
   - Secure the `/staff` and `/admin` routes using `<AuthorizeRouteView>` and `[Authorize(Roles = "...")]`.

## Phase 2: Staff Dashboard Enhancements
**Objective**: Complete the staff queue management capabilities.
1. **Recall Functionality**:
   - Add a `POST /staff/queue/{appointmentId:int}/recall` endpoint to `QueueManagement.Api`. This will change an appointment's status from `Missed` or `Called` back to `Waiting`.
   - Update `Staff.razor` UI to add a "Recall" action button.
2. **Role Restrictions**:
   - Decorate staff API endpoints (`/staff/*`) with `[Authorize(Roles = "Staff,Admin")]` to ensure only authorized staff can manipulate the queue.

## Phase 3: Admin Dashboard & Configuration
**Objective**: Provide administrators with tools to manage services and view real analytics.
1. **Service & Branch Configuration API**:
   - Add `POST`, `PUT`, `DELETE` endpoints for `/admin/services` and `/admin/branches` in the API.
   - Secure these endpoints with `[Authorize(Roles = "Admin")]`.
2. **Admin UI**:
   - Build sub-components in `Admin.razor` to list, create, edit, and delete Services and Branches.
3. **Analytics API**:
   - Replace the mocked `AverageWaitMinutes` in `staffApi.MapGet("/dashboard")` with a real calculation (average time difference between `CreatedAt` and `ServedAt` for the current day).
   - Create a new endpoint `/admin/analytics` to return Peak Hours (appointments grouped by hour of the day) and Service Demand Trends (appointments grouped by `ServiceId`).
4. **Analytics UI**:
   - Display the updated metrics and data trends in `Admin.razor`.

## Phase 4: Customer Portal & Real-Time Tracking Polish
**Objective**: Ensure customers have a seamless and accurate tracking experience.
1. **Tracking UI**:
   - After a successful booking, update `Customer.razor` to show the customer's active position in the queue (e.g., "There are 3 people ahead of you").
2. **SignalR Enhancements**:
   - When the Blazor client receives the `QueueUpdated` SignalR event, the customer page should automatically recalculate their specific position in line without requiring a full page refresh.
