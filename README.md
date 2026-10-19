# Primary Wallpaper Agent

Windows agent สำหรับแสดงภาพบน Windows Primary Display และให้จออื่นดำ โดยตาม primary/การเสียบถอดจออัตโนมัติ

## โครงสร้าง

- `src/PrimaryWallpaperAgent.Core/` — อ่าน display, Windows COM, policy และ engine ที่ใช้ร่วมกัน
- `src/PrimaryWallpaperAgent.Tray/` — Config, Tray และการบันทึก settings
- `src/PrimaryWallpaperAgent.Probe/` — Console แบบอ่านอย่างเดียว และคำสั่งทดลองที่ต้องเรียก explicit
- `tests/PrimaryWallpaperAgent.Tests/` — ชุดทดสอบ policy/config/localization/window เดิม แยกออกจากโปรแกรมใช้งาน
- `scripts/` — build, test, publish, launch และ diagnostics
- `assets/branding/` — ที่เก็บงานไอคอนในชุดถัดไป
- `docs/development/` — อธิบายโครงสร้างและงานพัฒนา
- `docs/testing/` — หลักฐานและขั้นทดสอบแต่ละ phase
- `artifacts/` — bin/obj และ package ใช้งาน ไม่เข้า Git
- `.local/` — SDK state และสำรองไฟล์ที่ย้าย ไม่เข้า Git

ไฟล์ `Directory.Build.props` กำหนด .NET 10, nullable, warnings-as-errors และ centralized artifacts ร่วมกันทั้ง solution ไม่มี NuGet package ภายนอก

## Build และทดสอบ (PowerShell)

```powershell
Set-Location -LiteralPath 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent'
.\scripts\Build.ps1
.\scripts\Test.ps1 -SkipBuild
```

ชุดทดสอบใช้ fake desktop/startup ไม่ตั้ง wallpaper หรือ registry จริง การทดสอบ window เปิดเฉพาะหน้าต่าง fixture ของโปรแกรมแล้วปิดเอง

## ใช้งาน

```powershell
.\scripts\Publish-Tray.ps1
.\scripts\Start-WallpaperTray.ps1 -Config
```

ปิด Tray ก่อน publish ซ้ำ Package ใช้งานยังอยู่ `artifacts/PrimaryWallpaperAgent-v0.2.0/` ตำแหน่งเดิม เพื่อคง settings, ภาพและ startup command ที่ผู้ใช้อาจตั้งไว้

คำสั่ง Probe แบบอ่านอย่างเดียว:

```powershell
.\artifacts\bin\PrimaryWallpaperAgent.Probe\debug\PrimaryWallpaperAgent.Probe.exe --json
```

Exit code 0 = complete, 1 = fatal error, 2 = มีรายการ API ที่ยังอ่าน/จับคู่ไม่ครบ ในเครื่องนี้เคยมี cached/unknown entries แม้ active displays จับคู่สำเร็จ

## ข้อมูลและความเข้ากันได้

`snapshots/` และ `.local-assets/` เดิมคงอยู่เพื่อรักษา absolute paths ใน trial/restore plans ไม่ย้ายหรือลบข้อมูลทดลองเก่า ส่วน bin/obj เก่าเก็บสำรองไว้ใต้ `.local/reorganization-*/legacy-build/`

รอบนี้จัด source/build/test layout โดยไม่ย้าย settings หรือภาพที่ Windows ใช้อยู่ การย้ายข้อมูลผู้ใช้ไปตำแหน่งที่ไม่ผูกกับ package version จะเป็น migration แยกที่ต้องตรวจ hash และ readback ก่อนใช้

สคริปต์ที่ root ย้ายเข้า `scripts/` แล้ว ส่วนคำสั่ง self-test เดิมย้ายไปตัวรัน `PrimaryWallpaperAgent.Tests.exe policy|config|localization|window` ไม่บรรจุ test code ในแอปใช้งาน

## สถานะและขั้นถัดไป

ผ่านการยืนยันจากผู้ใช้: primary switch, hotplug 3→1→3, การสลับ Virtual Desktops, Config visibility และการรับภาพเก่ากลับมาโดยไม่ถูกทับ

เพิ่ม Config ไทย/อังกฤษแล้ว ผู้ใช้ตรวจผ่านชุดภาษา ส่วนค่า poll ที่ปรับได้/ไอคอนจริง/Spotlight ยังไม่ได้เพิ่ม และยังต้องทดสอบ projector, มากกว่า 3 จอจริง และ startup หลัง logon ตามขั้นที่ตกลง

- [โครงสร้างและขอบเขตการย้าย](docs/development/PROJECT-LAYOUT.md)
- [ดัชนีผลทดสอบ](docs/testing/README.md)
- [Config และ regression fixes](docs/testing/CONFIG-TEST.md)
- [ภาษาไทย/อังกฤษและผลทดสอบ](docs/testing/LOCALIZATION-TEST.md)

Workflow: ส่งแต่ละชุดให้ผู้ใช้ตรวจก่อน commit เมื่อผู้ใช้อนุมัติหรือสั่งให้ทำงานต่อจึงค่อย commit และเริ่มชุดถัดไป