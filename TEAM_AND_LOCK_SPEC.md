# Team Management & Workflow Lock — Specification

Written for: you, to review the rules before and after the build. Each item in the checklist at the
end is ticked when it's implemented and verified.

---

## 1. What this adds

1. **Accounts and team members.** The person who creates an account can invite others to work in it,
   and choose exactly what each person may do.
2. **Lock.** Anyone with edit permission can lock a workflow. While locked, nothing about it can
   change and it can't be deleted, until someone with edit permission unlocks it.
3. **Clear feedback.** People without a permission see the control disabled, with a tooltip saying
   **"You have no access"**. A locked workflow shows a tooltip explaining why.

---

## 2. Core terms

| Term | Meaning |
|---|---|
| **Account** | A workspace. Its id is the id of the user who created it (its **owner**). |
| **Owner** | The creator of the account. Has every permission. Cannot be removed. |
| **Member** | Someone invited into an account, with a set of permissions. |
| **Invitation** | A pending invite: email + permissions + a secret link valid for 7 days. |
| **Permission** | One allowed action, for example `workflows.edit`. |
| **Preset** | A ready-made permission set: Viewer, Editor, Admin. |
| **Lock** | A flag on one workflow that freezes it. |

---

## 3. Permissions

| Permission | Allows |
|---|---|
| `workflows.view` | See workflows and open them read-only |
| `workflows.create` | Create new workflows |
| `workflows.edit` | Save changes, rename, turn Active on/off, **lock and unlock** |
| `workflows.run` | Run a workflow manually |
| `workflows.delete` | Delete a workflow (only when it is not locked) |
| `members.manage` | Invite people, change permissions, remove members, revoke invitations |

### Presets

| Preset | Permissions |
|---|---|
| **Viewer** | `workflows.view` |
| **Editor** | `workflows.view`, `workflows.create`, `workflows.edit`, `workflows.run` |
| **Admin** | all six |

The inviter can pick a preset, then tick or untick individual permissions. The owner always has all
six and can't be changed.

---

## 4. Locking rules

A workflow is **locked** when someone with `workflows.edit` locks it.

### While locked, these are refused with `423 Locked`

- Saving the graph, renaming, changing the description
- Turning Active on or off
- Manual runs
- **Webhook calls** (they are refused as well, so a locked workflow fires nothing)
- Deleting the workflow, **including by the owner**

### Still allowed while locked

- Viewing the workflow and its runs
- Unlocking (if you have `workflows.edit`)

### Lock and unlock

- **Lock:** requires `workflows.edit`. Locking an already locked workflow changes nothing.
- **Unlock:** requires `workflows.edit`. A person **without** that permission cannot unlock, even if
  they can see the workflow. They see the tooltip "You have no access".
- Each lock records who locked it and when. The workflow shows "Locked by Asha, 10:02".

### Why delete is blocked while locked

Locking is a safety switch. If delete were allowed, a lock could be bypassed by deleting and
re-creating. So a deletion always needs an unlock first.

---

## 5. Access rules (error responses)

| Situation | Status | Message |
|---|---|---|
| Not a member of the account | 403 | "You don't have access to this account." |
| Member without the needed permission | 403 | "You have no access to do this." |
| Workflow is locked | 423 | "This workflow is locked. Unlock it to make changes." |
| Workflow not found in this account | 404 | — |

Order of checks: find the workflow (404) → check permission (403) → check lock (423).

---

## 6. Invitations

1. An inviter with `members.manage` enters an email and a preset, and may adjust permissions.
2. The server creates a secret token, stores only its hash, and returns the **invite link once**.
   The inviter copies it and sends it themselves.
   *(No email service is connected yet, so the app doesn't send the email. The link is returned
   once and cannot be shown again.)*
3. The invitee opens the link, logs in or registers, and accepts.
4. The app accepts only when the logged-in user's email **exactly matches** the invited email
   (case-insensitive). Otherwise it refuses: "This invitation was sent to another email."
5. Invitations expire after 7 days, can be revoked, and can be accepted only once.
6. You cannot invite yourself, and you cannot invite an existing member (their permissions can be
   changed instead).

---

## 7. How a collaborator's work is scoped

- Every request can name the account it is working in with the header `X-Account-Id`. If it's
  missing, the user's own account is used.
- The server checks that the user is a member of that account, and loads their permissions.
- Workflows, runs, and webhooks all belong to the account owner, so a collaborator's work lands in
  the owner's account.
- Integration connections belong to the account owner, so a collaborator's workflows use the owner's
  connected Google account. (Collaborators cannot use their own Google account inside the account.)

---

## 8. Data model

Permissions are stored as rows, not as JSON text, so they can be read and changed in SQL Server.

| Table | Purpose | Columns |
|---|---|---|
| `AccountMemberships` | Who is in which account | Id, AccountOwnerId, UserId, InvitedByUserId, CreatedAt |
| `Invitations` | Pending invites (token stored as a hash) | Id, AccountOwnerId, Email, TokenHash, InvitedByUserId, ExpiresAt, AcceptedAt, RevokedAt, CreatedAt |
| `WorkflowLocks` | Locked workflows (a row means locked) | WorkflowId (primary key), LockedByUserId, LockedAt |
| `Permissions` | **Catalog** of every permission | Key (primary key), Label, Description, SortOrder |
| `PermissionPresets` | Named bundles (Viewer, Editor, Admin) | Id, Name (unique), Description, SortOrder |
| `PermissionPresetItems` | Mapping: which permissions a preset contains | PresetId + PermissionKey (primary key) |
| `MembershipPermissions` | Mapping: which permissions a member holds | MembershipId + PermissionKey (primary key) |
| `InvitationPermissions` | Mapping: which permissions an invite will grant | InvitationId + PermissionKey (primary key) |

### Changing permissions from the database
- **Rename a permission label or description:** update `Permissions`. The UI shows it on the next load.
- **Change what a preset includes:** insert or delete rows in `PermissionPresetItems`. New invites
  use the new set. Existing members keep what they were given.
- **Add a new preset:** insert into `PermissionPresets`, then its items. It appears in the invite form.
- **Change one member's permissions:** edit rows in `MembershipPermissions`.
- **A new permission key** must also be added to `Permissions`, and needs code that checks it, or
  nothing enforces it.
- Keys are protected by foreign keys: a permission that is in use cannot be deleted from
  `Permissions`. Remove its member rows first.

Starting data is in [PermissionSeed.cs](backend/WorkflowPlatform.Api/Data/PermissionSeed.cs) and the
`NormalizePermissions` migration. The old JSON columns were copied into the mapping tables in that
migration, and the copy was checked: 11 membership grants and 19 invitation grants, matching the old data.

---

## 9. Schema change: migrations

The app used `EnsureCreated()`, which doesn't add tables to an existing database. It moves to EF
Core migrations:

1. `InitialSchema` — the current schema, as a baseline.
2. `AddCollaboration` — the three new tables above.
3. The existing `Monkmap` database is marked as already at `InitialSchema` (so no data is lost),
   then `AddCollaboration` is applied.
4. A brand-new database gets both migrations automatically on first start.

**Trade-off:** migrations are generated for SQL Server. Other database providers would need their
own migration set.

---

## 10. API

| Method | Path | Permission | Purpose |
|---|---|---|---|
| GET | `/api/accounts` | any member | accounts I belong to, with my permissions |
| GET | `/api/account/members` | any member | list members |
| POST | `/api/account/invitations` | `members.manage` | create invitation → returns link once |
| GET | `/api/account/invitations` | `members.manage` | pending invitations |
| DELETE | `/api/account/invitations/{id}` | `members.manage` | revoke |
| PUT | `/api/account/members/{userId}` | `members.manage` | change permissions |
| DELETE | `/api/account/members/{userId}` | `members.manage` | remove member |
| POST | `/api/invitations/accept` | logged in | accept with token (email must match) |
| POST | `/api/workflows/{id}/lock` | `workflows.edit` | lock |
| POST | `/api/workflows/{id}/unlock` | `workflows.edit` | unlock |

Existing workflow endpoints keep their paths and now apply the permission and lock checks.
Workflow summaries and details include `isLocked`, `lockedBy`, and `lockedAt`.

---

## 11. Frontend behaviour

- **Account switcher** in the header, shown when the user belongs to more than one account. The
  choice is sent as `X-Account-Id`.
- **Team page** (`/team`): members and their permissions. Invite and edit controls appear only with
  `members.manage`.
- **Invite link** (`/invite?token=…`): saved, then accepted after login or registration.
- **Workflow list:** lock badge on locked workflows. Delete is disabled when locked or without the
  permission. "New workflow" is disabled without `workflows.create`.
- **Editor:** Lock/Unlock button, Save, Run, Active toggle, and node editing are all disabled when
  the user lacks permission or the workflow is locked. A banner explains a lock.
- **Every disabled control** has a tooltip: "You have no access" when the permission is missing, or
  "Locked — unlock it to make changes" when locked.

---

## 12. Checklist — status

Legend: **[x]** built and verified by an automated test or a build. **[~]** built, but not yet
verified by a browser walkthrough. **[ ]** not done.

Automated checks: `tools/monkmap-test-client/test_team_and_locks.py` — **44 / 44 passing**.

### Backend
- [x] Permission catalog, presets, and member/invite grants stored as database tables — migrated (`NormalizePermissions`), 11 + 19 grants carried over, all 44 tests pass
- [x] Changing a preset in SQL changes what new invites get, shown through `GET /api/permissions` and an invite — verified, then reverted
- [x] Permission constants in code (keys only) — used by every check
- [~] Team page reads permission labels and presets from `GET /api/permissions` — built; typechecks and builds
- [x] `AccountMembership`, `Invitation`, `WorkflowLock` entities and mapping — tables verified in SQL Server
- [x] Migrations: `InitialSchema` baseline and `AddCollaboration` — both applied on a fresh database
- [x] Existing database marked at baseline, then migrated — 17 users and existing rows still present
- [x] `CurrentAccount` resolves account and permissions per request (`X-Account-Id`) — membership and non-member tests
- [x] Workflow list, get, create, save, run, delete scoped to the account — viewer/owner/stranger tests
- [x] Permission checks on every workflow endpoint (403) — viewer and editor tests
- [x] Lock and unlock endpoints (edit permission) — editor locks, viewer cannot lock or unlock
- [x] Locked workflows refuse save, activation, manual runs, webhooks, and delete (423) — including by the owner
- [x] Executions scoped to the account — viewer sees owner's runs; non-member gets 403
- [x] Invitation create, list, revoke, with hashed token and 7-day expiry — duplicate, revoke, and list tests
- [x] Invitation accept with exact email match — wrong-email, reuse, and revoked tests
- [x] Member list, change permissions, remove (owner protected) — owner/viewer/editor tests
- [x] Cannot grant permissions you don't have (403) — manager tries Admin preset and is refused
- [x] `/api/accounts` returns memberships and permissions — viewer and owner account list tests

### Frontend
- [~] API client sends `X-Account-Id` from the selected account — built; typechecks
- [~] Account switcher in the header — built; typechecks
- [~] Team page (members, invitations, invite form with presets, edit, remove) — built; typechecks
- [~] Invite acceptance flow (`/invite` saves the token, accepted after login) — built; typechecks
- [~] Workflow list: lock badge, disabled delete and create with tooltips — built; typechecks
- [~] Editor: lock/unlock, disabled controls with tooltips, locked banner, read-only canvas — built; typechecks and production build passes

### Verification
- [x] Backend builds
- [x] Frontend typechecks and builds for production
- [x] Fresh database created from migrations
- [x] Existing database baselined and migrated, existing data still present
- [x] Owner creates account, invites a viewer and an editor
- [x] Viewer cannot edit, run, or lock (403 with the message)
- [x] Editor can edit and lock; viewer cannot unlock
- [x] Locked workflow refuses save, run, webhook, and delete (423), including by the owner
- [x] Unlocked workflow deletes successfully
- [x] Invitation for another email is refused; a revoked or reused invite is refused
- [ ] Browser walkthrough: log in as each role and click through the tooltips, Team page, and invite link. **Not done yet**, so the frontend items above stay "built" until it is.

### Known limits (not bugs, but worth knowing)
- Invitations are not emailed yet. The inviter copies the link, which is shown once.
- A collaborator's workflows use the account owner's connected Google account, not their own.
- A run that was already queued before a lock still finishes. Locks stop new runs and changes.
- Migrations are generated for SQL Server. Other database providers would need their own migration set.
- The Active switch is not checked by the webhook (existing behaviour, unchanged).
