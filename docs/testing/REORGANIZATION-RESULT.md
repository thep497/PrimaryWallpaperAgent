# ผลตรวจหลังจัดโฟลเดอร์ — 19 ตุลาคม 2569

สถานะ: รอผู้ใช้ตรวจ ยังไม่ commit ชุดจัดโครงสร้าง

## สิ่งที่ตรวจผ่าน

- ย้ายและตรวจ SHA-256 ก่อน/หลัง 38 ไฟล์ก่อนแก้ path/reference
- Solution มี Core library, Probe console, Tray WinForms และ standalone Tests รวม 4 projects
- Debug build ทั้ง solution: 0 warnings / 0 errors
- ชุด policy/config/window เดิมผ่าน รวม A-B-A regression, rollback และ window reveal เมื่อเปิดแบบ Hidden
- Native JSON จาก Probe เก่าที่เก็บสำรอง เทียบกับ Probe ใหม่ตรงกันทุก field ยกเว้น CapturedAt: active displays 3, mapping/topology ตรงกัน
- scripts/Restore-WallpaperTrial.ps1 -CheckOnly ผ่าน: hash ภาพทดลอง, mapping, shared COM lifetime และ original-image availability โดยไม่เรียก setter
- scripts/Inspect-WallpaperTrial.ps1 บันทึก read-only snapshot ที่ root snapshots เดิมได้ แผน trial เดิมยังอ่านได้
- native event self-test ผ่าน: 17 samples, LOCAL_SELF_TEST delivered, state transitions 0, dropped 0, wallpaper setters 0
- Release publish ผ่าน และ dependency ของ production package ไม่มี Tests assembly
- Archived obsolete PrimaryWallpaperAgent.exe/dll และ metadata เดิมไว้ใน .local โดยไม่ลบ ภายใน package เหลือ entry point Tray กับ Core ที่ใช้จริง
- Settings/current photo/black file มี SHA-256 เดิมครบหลัง publish
- Package smoke test: 5.08 วินาที, 4 samples, repairs 0, exit 0
- เปิด Config จาก package ใหม่ได้จริง: nativeVisible=True และ Windows มี main-window title/handle

## ค่าที่ไม่ใช่ regression

Probe ยังคืน exit 2 เพราะ 7 API entries เดิมไม่สมบูรณ์ ทั้งที่ active displays 3 จับคู่ได้ ไม่เปลี่ยนการจัดประเภทข้อมูลนี้เพื่อให้ผลดูผ่าน

trial-status เทียบแผน Phase 2 เก่าคืน POLICY_READBACK_MATCH=False และ ORIGINAL_READBACK_MATCH=False เพราะตอนนี้ใช้ Config และภาพสำเนาใน package แทน assignment ใน trial เดิม ไม่ได้ใช้ผลนี้ตัดสิน policy ปัจจุบัน

## หลักฐานเฉพาะเครื่อง (ไม่เข้า Git)

- .local/reorganization-20261019_113807/source-moves.json
- .local/reorganization-20261019_113807/native-checks.json
- .local/reorganization-20261019_113807/package-checks.json
- snapshots/virtual-desktop-ReorgCheck-20261019_114332_512.json
- snapshots/event-probe-20261019_114332_812-1bba51f8/
- runtime ภายใน package เดิมสำหรับ smoke / Config log

คง package/settings/ภาพ/snapshots/.local-assets ที่ตำแหน่งเดิม ส่วน source, test, script และเอกสารใช้ตำแหน่งใหม่ตาม README ไม่ตั้ง startup เพิ่ม ไม่เปลี่ยน poll/language/icon ในชุดนี้