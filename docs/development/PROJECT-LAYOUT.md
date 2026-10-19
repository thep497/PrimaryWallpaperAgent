# Project layout — ขั้นจัดโฟลเดอร์

## Dependency

`Tray -> Core`, `Probe -> Core`, `Tests -> Core + Tray` ไม่มี production project อ้าง Tests หรือ Console executable

Core เป็น Windows-targeted library ไม่ได้อ้างว่ารองรับ OS อื่น แยก Interop/Displays/Policies/Trials/Diagnostics ตามหน้าที่โดยคง namespace และ logic เดิม

Tray เก็บ UI, Configuration และ Integration แยก folder; ภายในยังใช้ internal types และให้ friend access เฉพาะ Probe/Tray/Tests ที่ต้องใช้

Tests เป็นตัวรัน console ที่ใช้ WinForms สำหรับตรวจ Config จริง มี policy/config/window suites เดิม ไม่เพิ่ม test framework หรือ NuGet package ในขั้นนี้

## Build output

`Directory.Build.props` เปิด UseArtifactsOutput ตาม .NET SDK:
- `artifacts/bin/<project>/<debug|release>/`
- `artifacts/obj/<project>/<debug|release>/`
- package ใช้งานคง `artifacts/PrimaryWallpaperAgent-v0.2.0/` เดิมในขั้นนี้

scripts/Common.ps1 หาตำแหน่ง solution จาก parent ของ scripts แบบกำหนดแน่นอน ไม่ค้น parent/sibling/drive และให้ทุก diagnostic ใช้ probe path เดียวกัน

## สิ่งที่ย้ายและสิ่งที่เก็บไว้

ย้าย source 3 โปรเจกต์, tests 1 โปรเจกต์, scripts และเอกสาร testing ออกจาก root มี manifest SHA-256 ของทุกไฟล์ก่อน/หลังย้ายใน .local/reorganization-*/source-moves.json

bin/obj ที่เคยสร้างทั้ง root และ Tray ถูกย้ายเก็บใต้ .local/reorganization-*/legacy-build หลังตรวจ absolute source/destination ว่าอยู่ภายใน project ไม่ลบ recursively

รักษา snapshots/, .local-assets/, plans/ (ถ้ามี) และ .cli-home/ เดิม ไม่แก้ absolute paths ในแผนย้อนหลัง ไม่ย้าย settings/ภาพ/package runtime ในขั้น source layout นี้

README เดิมเก็บเป็น docs/testing/PROBE-GUIDE.md เพื่อรักษาคำอธิบายผล Probe และแหล่งอ้างอิง ส่วน README ใหม่ที่ root เป็นทางเข้าปัจจุบัน

คำสั่งทดสอบแยกจาก production:
- เดิม --self-test-trial -> Tests.exe policy
- เดิม --self-test-config -> Tests.exe config
- เดิม --self-test-window -> Tests.exe window
- --self-test-events ของ Probe ยังอยู่ เพราะเป็น diagnostic native observer แบบอ่านอย่างเดียว
- --smoke-test ของ Tray ยังอยู่ เพื่อตรวจ binary ที่ส่งใช้งานโดยไม่เรียก setter

## การตรวจขั้นนี้

Build ทั้ง solution, policy/config/window regression tests, probe JSON เทียบ binary เก่า, ตรวจ restore plan แบบอ่านอย่างเดียว, native event observer self-test, publish และตรวจ Tray/Config จาก package ใหม่

ผลจริงบันทึกใน docs/testing/REORGANIZATION-RESULT.md หลังตรวจเสร็จ ชุดนี้รอผู้ใช้ตรวจ ก่อน commit และก่อนเพิ่มฟีเจอร์ที่เหลือ

## ขั้นที่แยกออกมา

การย้ายข้อมูลใช้งานจริงออกจาก package version ต้องมี migration ของ settings/สำเนาภาพ/known current aliases พร้อม rollback จึงไม่ปนกับการย้าย source ในครั้งนี้

อ้างอิง: [.NET artifacts output](https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output), [dotnet solution](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-sln)