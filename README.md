# DEGMA

A patient and treatment tracker for a weight-loss clinic. DEGMA records patients and their weekly visit reports (weight, dosage, body composition, vitals and side effects), charts progress, and produces PDF reports for patients. It runs as a single Windows executable on the clinic PC and works offline.

Built with ASP.NET Core MVC (.NET 10), Entity Framework Core and SQLite.

## Features

- **Patients:** a searchable, sortable directory. Each profile has stats, a weight curve and the full treatment history. Double-click a visit to see it in full.
- **Visit reports:**
  - weight with the change since the previous visit;
  - dosage and batch number;
  - visceral fat and muscle mass;
  - pulse and blood pressure;
  - side effects (from a standard dictionary, plus free text) and notes.
- **Reports review:** a day-by-day list of every entry. It flags likely typos and out-of-range vitals, using limits you can change.
- **Dashboard:** headline stats, progress and activity charts, dose breakdown and current batch usage.
- **PDF reports:** preview, download or email a patient's report. PDFs are printed with Microsoft Edge in headless mode.
- **CSV import:** bulk-load patients and reports, with a review step for invalid rows and duplicates.
- **Backups:** a daily automatic backup to a local folder and a cloud-synced folder, plus manual backup, restore and download.
- **Roles:**
  - **Doctor** has full access.
  - **Receptionist** can view, but can't create, edit or delete clinical data.

## Requirements

| To | You need |
|---|---|
| Run a release | Windows 10/11 (x64), Microsoft Edge (for PDFs) |
| Build from source | [.NET 10 SDK](https://dotnet.microsoft.com/download), optionally Visual Studio 2026 |

## Getting started

### Run a release

1. Download `DEGMA.exe`, `wwwroot.zip` and `appsettings.json` from the latest [release](https://github.com/ConJaBst/DEGMA/releases/latest).
2. Put them in one folder and extract `wwwroot.zip` there.
3. Edit `appsettings.json` (see [Configuration](#configuration)).
4. Run `DEGMA.exe` **from that folder**, then open http://localhost:5000.

The database (`degma.db`) is created in the working directory on first start. The schema is upgraded automatically when a new version starts. **Back up before upgrading.**

### Run from source

```bash
git clone https://github.com/ConJaBst/DEGMA.git
cd DEGMA
dotnet run --project DEGMA --launch-profile http
```

The app runs at http://localhost:5186 and uses `DEGMA/degma.db`.

### Default accounts

These are created on first start:

| Role | Email | Password |
|---|---|---|
| Doctor | `doctor@degma.com` | `Doctor123` |
| Receptionist | `receptionist@degma.com` | `Receptionist123` |

> **Security:** these are well-known defaults, and there is no in-app way to change them yet. Keep the app on the clinic PC (`localhost`) and don't expose it to a network.

## Configuration

Settings live in `appsettings.json` next to the executable.

| Section | Purpose |
|---|---|
| `EmailSettings` | SMTP server and sender used to email PDF reports. For Gmail, use an [app password](https://support.google.com/accounts/answer/185833). |
| `ClinicSettings` | `CurrentBatchNumber`, the default batch on new reports (also editable from the dashboard). |
| `BackupSettings` | `LocalFolder`, `CloudFolder` (e.g. a OneDrive folder, or empty to skip), `DailyTime` (`HH:mm`) and `KeepDays` (`0` keeps everything). Environment variables such as `%OneDrive%` are expanded. |
| `ReportChecks` | Limits that flag values on the Reports page and visit readout: weight change, visceral fat and muscle mass ranges and jumps, and pulse and BP High/Low. Optional; the defaults are built in. |
| `Pdf:EdgePath` | Optional. The full path to `msedge.exe`, if Edge isn't in its standard location. |

> **Never commit a real `SmtpPassword`.** The committed `appsettings.json` must keep it blank.

## CSV import

From the dashboard, upload a CSV with a header row and these columns in order:

`Id, Firstname, Surname, DateOfBirth, Email, StartingWeight, TreatmentStartDate, (unused), ReportNumber, WeekNumber, Date, Weight, Dosage, WeightChange, Notes, SideEffects, VisceralFat, MuscleMass, BatchNumber, Pulse, SystolicBp, DiastolicBp`

- The first 15 columns are required. The rest are optional.
- Separate multiple side effects with `|`.
- Numbers always use a `.` decimal point, whatever the PC's regional settings are.

## Building a release

```powershell
.\publish.ps1
```

This builds a self-contained, single-file `DEGMA.exe` with the `FolderProfile` publish profile, into `DEGMA\bin\Release\net10.0\publish\`. Attach `DEGMA.exe`, a zip of the `wwwroot` folder and `appsettings.json` (with a blank `SmtpPassword`) to a GitHub release.

## Project structure

```
DEGMA/
├── Controllers/   MVC controllers (Patients, Reports, Import, Backup, …)
├── Data/          DbContext, side effect dictionary, invariant-culture model binders
├── Migrations/    EF Core migrations (applied automatically on start)
├── Models/        Entities, view models and settings classes
├── Services/      PDF, email and backup services
├── Views/         Razor views
└── wwwroot/       CSS, JS and bundled libraries (Bootstrap, Chart.js, jQuery)
publish.ps1        Release build script
```

## Development

- **Branches:**
  - `production` is the default branch and holds what ships to the clinic.
  - `master` is the dev branch. Work on a feature branch, open a pull request into `master`, and promote `master` to `production` with a pull request when it's ready to release.
- **Schema changes:** add an EF Core migration:
  ```bash
  dotnet ef migrations add <Name> --project DEGMA
  ```
- **Test data:** to test against a copy of the database, run with a different working directory:
  ```bash
  dotnet run --project DEGMA -p:RunWorkingDirectory=<folder with a copy of degma.db>
  ```
- **Offline:** the clinic PC may be offline, so bundle front-end libraries under `wwwroot/lib` rather than loading them from a CDN.

## Dev site

Every push to `master` is deployed to an Azure App Service web app by `.github/workflows/deploy-dev.yml`.

- **Environment:** the dev site runs as `ASPNETCORE_ENVIRONMENT=Dev`, so it uses `appsettings.Dev.json`. It has its own doctor password, set by `SeedUsers:DoctorPassword`:

  | Role | Email | Password |
  |---|---|---|
  | Doctor | `doctor@degma.com` | `Developer1` |

- **Data resets:** each deploy replaces the database with the committed test database, so dev data resets on every push.
- **Not available on the dev site:** PDF reports (there's no Edge on App Service) and email (no SMTP password).

One-time setup:

1. **Create the web app:** in Azure, create a web app with publish *Code*, runtime stack *.NET 10*, operating system *Windows*, and the *Free F1* pricing plan.
2. **Get the publish profile:** in the web app, open *Configuration > General settings* and turn on *SCM Basic Auth Publishing Credentials*. Then use *Overview > Download publish profile*.
3. **Connect GitHub:** in GitHub, open *Settings > Secrets and variables > Actions*.
   - Add a secret `AZURE_WEBAPP_PUBLISH_PROFILE` with the contents of the publish profile file.
   - Add a variable `AZURE_WEBAPP_NAME` with the web app's name.
4. **Deploy:** re-run the *Deploy dev site* workflow, or push to `master`.

Until the secret is set, the workflow builds the app but skips the deploy, with a warning.

## Resources

- [Project file (SharePoint)](https://advtechonline-my.sharepoint.com/:u:/g/personal/st10290665_vcconnect_edu_za/IQAWfQdkyPFoSKFLO7AGHM2RAdPjjEte3oVhSdbZCbRM44M?e=BXauan)
