# ผลตรวจ Phase 1 — 19 ตุลาคม 2569

## ข้อเท็จจริงจากการรันบน Windows ของผู้ใช้

- SDK 10.0.402; runtime .NET 10.0.13; Windows build 26300; interactive session 1
- Build แบบ TreatWarningsAsErrors ผ่าน: 0 warnings / 0 errors
- GDI รายงาน active display 3 จอ; wallpaper API คืน 10 รายการ
- `DISPLAY1`: LEN41AF, 1920×1200 ที่ (-1920,0), secondary
- `DISPLAY2`: LG GSM76F5, 1920×1080 ที่ (0,0), **primary**
- `DISPLAY3`: RTD0002, 1600×2560 ที่ (1920,-555), secondary
- จอที่ใช้งานทั้ง 3 จับคู่ด้วย interface path แบบ exact ได้ และ rectangles จากสอง API ตรงกัน
- ทั้ง 3 รายการ active คืน wallpaper path เดียวกัน (ชื่อไฟล์ `wp_20121231_100.jpg`); ผู้ใช้ยืนยันแล้วว่าภาพตรงกันทั้ง 3 จอและ LG เป็น primary; โปรแกรมไม่ได้เปิดอ่านไฟล์ภาพ
- Background color API คืน `#000000`; Position=Fill; slideshow status=1
- Topology ไม่เปลี่ยนระหว่าง capture แต่ละรอบ

## รายการที่ต้องตรวจต่อ

- นอกเหนือจาก 3 จอปัจจุบัน มี 7 รายการที่จับคู่ไม่ได้
- 6 รายการมี ID แต่ `GetMonitorRECT` คืน `E_FAIL (0x80004005)` ไม่ใช่ `S_FALSE` ตามกรณี detached ในเอกสาร จึงระบุสถานะเป็น UNKNOWN
- อีก 1 รายการคืน ID ว่าง ใน baseline แรกบันทึกพฤติกรรมไว้แล้ว; โปรแกรมฉบับถัดมาไม่เรียก per-monitor getter สำหรับ ID ว่าง และบันทึก HRESULT เป็น null (ไม่ได้เรียก API)
- รายการส่วนเกินอาจเกี่ยวกับ display history แต่ยังไม่มีหลักฐานพอจะสรุปว่าเป็น detached/cached ทั้งหมด ไม่อ่านหรือแก้ registry เพื่อพิสูจน์ในขั้นนี้
- Exit code 2 / Complete=false สื่อว่ามีรายการ API ที่ยังอ่านไม่ครบ แม้ mapping ของ active display ทั้ง 3 จะสำเร็จ
- `snapshots/phase1-baseline.json` เก็บผลรอบแรก; `snapshots/phase1-verified.json` เก็บผลหลังเพิ่ม guard สำหรับ ID ว่าง ไม่เขียนทับ baseline

## ยังไม่ได้ทดสอบ

- Projector และจอจริงมากกว่า 3 จอ ยังไม่ได้ทดสอบ (การจำลอง 5 จอไม่ใช่หลักฐานฮาร์ดแวร์จริง)
- Notebook เดี่ยว, ถอด/เสียบจอ 3 -> 1 -> 3 และสลับ primary ผ่านแล้ว ดู HOTPLUG-TEST.md และ CONSOLE-GUARD-TEST.md
- Virtual Desktops: อ่าน A/B, apply แยกจอ, bounded Guard และการสลับครบ 3 desktops ผ่านการยืนยันจากผู้ใช้แล้ว ดู VIRTUAL-DESKTOP-TEST.md / EVENT-PROBE-TEST.md; ยังไม่ได้ทดสอบการเลือกภาพต่างกันในแต่ละ desktop
- Tray และ autostart ยังไม่ได้ทดสอบ

ต้องผ่านการตรวจ Virtual Desktops ก่อนเพิ่ม watcher/tray/autostart ตามขอบเขตที่ตกลงกัน
