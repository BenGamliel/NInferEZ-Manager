# סקירת קוד, יעילות ותחזוקה — משפחת NInferEZ

תאריך: 30 בספטמבר 2026. סוג העבודה: ביקורת קוד ומבנה אחסון בלבד, ללא תיקון קוד וללא הרצת מודלים או בדיקות GPU.

## 1. מסקנה מנהלית

אין הצדקה כרגע לכתוב מחדש את המנוע, להחליף טכנולוגיית ממשק או להתחיל את האפליקציה מחדש. יש בסיס שימושי: מנוע עצמאי, הפרדה בין UI ל־backend, טעינה עצלה, קישור מודלים ללא העתקה, הפצות נפרדות, קטלוגים מרוחקים ופרופילים. עם זאת, יש כמה ליקויים במסלולים חשובים שלא נכון להתייחס אליהם רק כאל ״אופטימיזציה״: אימות readiness עם API key, סגירת קבצים בהורדות, אבטחת Control API, תיאום בקשות ופריקת מודל, ועדכון Portable ללא rollback מלא.

ההמלצה היא **ייצוב ממוקד ואז refactor הדרגתי**. להחליף או לארגן מחדש חלקים קטנים בעלי אחריות ברורה, לשמר את הפרופילים והמנוע שנבדקו, ולהוסיף בדיקות host-only סביב המסלולים המסוכנים. לא להוסיף מערכת plugins, שירות Windows, תשתית ענן או שכבת abstraction כללית ללא צורך מוכח.

מבחינת אחסון, קוד מת אינו הגורם העיקרי. המשקל הגדול נמצא במודלים, חומרי המרה ותוצרי קומפילציה. מבחינת ביצועי inference, אין בבדיקה זו ראיה שהמנוע עצמו דורש החלפה או ש־UI אחר ישפר tok/s. יש ראיות לשכבות ניהול שעלולות להוסיף I/O, לעכב readiness או לפגוע באמינות, במיוחד בבקשות גדולות ובשינוי הגדרות בזמן ריצה.

## 2. תחום הבדיקה ורמת הוודאות

### 2.1 מה נבדק

- `TEMP/NInferEZ Manager`: מקור האפליקציה המלאה, Contracts, Backend, WinUI, בדיקות, בנייה, Installer, הפצת `0.0.1` האחרונה. `final/build-info.json` מצביע על `dev-015`.
- `TEMP/NInferEZ Suite/NInferEZ`: האפליקציה הקלה; שירותי פרופילים, inspection, ניהול תהליך, API עצל, ספריית מודלים, לוגים, בדיקות ואריזה.
- `TEMP/NInferEZ Suite/NInferEZ Engine`: CMake, בנייה ואריזה, provenance, חוזה ההפצה, inspector וחלקי HTTP/CLI הרלוונטיים לשילוב. קיימים builds ל־sm86, sm89 ו־sm120a.
- `TEMP/NInfer Manager WinUI`: מקור האפליקציה הישנה, חלקים משותפים, ותוצרי `1.1.0` שנותרו.
- `TEMP/NInfer Manager Next`: תוכנית בלבד; אין כאן אפליקציית Next ממומשת שאפשר להעיד על תקינותה.
- תיקיות מחקר ומנועים שנשמרו עבור Next: בעיקר מיפוי תפקיד ונפח, לא audit מלא של כל קוד המחקר.

זו סקירה רחבה של רכיבי הניהול ושל הגבולות בין המוצרים, **לא אימות שורה־שורה של כל CUDA kernel, ספריית צד שלישי או ענף מחקר**. עץ `src` של המנוע כולל מאות קבצים; אישור מתמטי/ביצועי של כולם דורש עבודה נפרדת.

### 2.2 מה לא בוצע

לא שונו מקורות, מנועים, פרופילים או חבילות; לא נמחקו קבצים; לא בוצעה הורדת מודל, התקנת עדכון, בדיקת GPU או inference. נוצר רק מסמך זה. לא נמדדו CPU/RAM/TTFT חדשים ולא הובטחה תאימות לכרטיס שלא נבדק. לא בוצעה כאן בדיקה מחדש של מצב GitHub בזמן אמת.

בדיקות הקוד הקיימות נקראו אך לא הורצו במסגרת הסקירה. נתון ״65 hashes verified״ מגיע ממסמך build קיים, ולא מוכיח שמסלולי הורדה, עדכון, ביטול או inference נבדקו מקצה לקצה.

### 2.3 פירוש הסיווגים

- **מוכח במקור:** התנאי, המסלול או חסר הבדיקה נראים ישירות בקוד. עדיין ייתכן שצריך שחזור כדי למדוד השפעה במחשב מסוים.
- **סיכון מבוסס:** אפשרות תקלה נובעת מתזמון, קלט או סביבת Windows; אין טענה שהתרחשה אצל המשתמש.
- **הזדמנות:** שיפור אפשרי שצריך למדוד לפני אימוץ.
- **P0:** גבול אבטחה שיש לסגור לפני הפצה רחבה או שימוש רשת לא מוגבל.
- **P1:** תקלה פונקציונלית או אמינות במסלול מרכזי; לטפל לפני הרחבת פיצ'רים.
- **P2:** יעילות, תחזוקה, תאימות או UX חשובים אך לא חסם מוחלט לכל שימוש.
- **P3:** ניקוי קטן או ניסוי שלא מצדיק לשבור בסיס עובד.

## 3. הבסיס שכדאי לשמר

1. **לא נטען מודל רק כדי להציג את רשימת המודלים.** Manager מחזיר `/v1/models` מהספרייה המקומית. זו בחירה נכונה למנוחה ללא שימוש ב־VRAM של המודל.
2. **המנוע נשאר process עצמאי.** UI לא צריך לבצע CUDA בעצמו; אין סיבה להכניס kernels לשכבת הממשק.
3. **קישור קבצים במקום העתקת משקלים.** זה מתאים לדרישה לחסוך מקום.
4. **הפרדה בין ארכיטקטורות בהפצת המנוע.** build, manifest ו־hash נפרדים מועילים יותר מבינארי כללי ענק.
5. **אריזת מנוע כוללת אימות SHA-256, הגבלות גודל ו־staging.** לא מדובר במערכת הורדה חסרת הגנות; צריך להשלים את הגבולות החלשים.
6. **החלון מפסיק polling כאשר הוא מוסתר ב־Tray.** ב־Manager, `MinimizeToTray` עוצר `_timer`; אין צורך לדרוש מנגנון שכבר קיים.
7. **יש cache של 10 שניות למדידת GPU.** לא מופעל `nvidia-smi` מחדש בכל tick במצב התקין.
8. **יש provenance ורישיונות למנוע.** `UPSTREAM.md` מתאר fork דק של `iamwavecut/ninfer-all` ומפרט שינויי Windows. לשמר את ההיסטוריה והקרדיטים.
9. **יש בדיקות ללא GPU.** האפליקציה הקלה כוללת בדיקות profile, archive traversal, control token ו־proxy; Manager כולל בדיקות חוזה, CLI, aliases ו־API לפני טעינה. זה בסיס טוב להרחבה.
10. **יש כבר inspector ללא GPU במנוע.** הוא יכול לספק מידע אמיתי על artifact במקום להסיק יכולות משם קובץ.

## 4. יעילות עבודה: אמינות, מהירות ומניעת עבודה מיותרת

### W01 — readiness נכשל כאשר מוגדר API key — P1, מוכח במקור

מקור: `src/NInferManager.Backend/EngineController.cs:217`; מול `NInferEZ Engine/src/serve/http_server.cpp:407`.

Manager מוסיף `--api-key` לתהליך, אבל בדיקת readiness שולחת `GET /v1/models` בלי Authorization. במנוע הנוכחי `/health` מוחרג מאימות, אך `/v1/models` אינו מוחרג. לכן עם key לא ריק מנוע שכבר עלה יכול להשיב 401 בכל בדיקת readiness, להישאר מוצג כ־Loading, ולבסוף להיהרג אחרי timeout של 15 דקות.

זו אינה סיבה להאריך שוב timeout. צריך probe שתואם לחוזה readiness: למשל בדיקת endpoint מתאים עם credential פנימי, תוך אימות שהמודל באמת מוכן ולא רק שהתהליך חי. אסור לשנות באופן עיוור ל־`/health` בלי לבדוק את משמעות הסטטוס שלו.

בדיקת קבלה: מנוע HTTP מדומה המחייב key ב־models, הצלחה עם key, 401 מיידי שמסווג נכון, model-loading לעומת ready, וביטול ללא תהליך יתום. אין צורך ב־GPU לשחזור גבול האימות.

### W02 — אימות הורדה כשהקובץ עדיין פתוח לכתיבה — P1, מוכח במקור

מקור: `ModelDownloadService.cs:49,67,70`; `UpdateService.cs:46,50,52`.

בשני השירותים `await using var output` נשאר חי עד סוף ה־scope. לאחר flush פותחים את אותו קובץ דרך `File.OpenRead`, ואז מנסים `File.Move`, לפני Dispose של output ושל verify.

ב־UpdateService הכתיבה נפתחת עם `FileShare.None`, ולכן פתיחת verify מתנגשת עם handle הכתיבה. ב־ModelDownloadService הכתיבה משתמשת ב־`FileShare.Read`, אך handle האימות נפתח כברירת מחדל בלי לאפשר שיתוף write; נוסף לכך handles אלה אינם מאפשרים delete-sharing הדרוש למסלול rename רגיל ב־Windows. flush אינו סוגר handle. זהו מסלול בעייתי גם כשההורדה וה־hash נכונים.

המלצה: שלושה scopes מפורשים: הורדה וסגירת writer; אימות וסגירת reader; commit של הקובץ. לשמור partial רק בהתאם לסיבת הכשל. לשקול SHA במהלך הורדה בהמשך, לא במקום תיקון lifetime.

בדיקת קבלה: שרת מקומי עם קובץ קטן, הורדה מלאה, resume, hash נכון/שגוי, ביטול, rename וללא handles פתוחים. לבצע ב־Windows; זה host-only ולא דורש מודל אמיתי.

### W03 — החלפת הגדרות בזמן שהמנוע פעיל אינה אטומית — P1, מוכח במקור וסיכון מבוסס

מקור: `SettingsService.cs:20`; `EngineController.cs:27–28`; `MainWindow.xaml.cs:511`; `PublicApiProxy.cs:83`.

ה־Controller קורא `Port` ו־ActiveModel מתוך settings חיים. process שכבר רץ נשאר עם port, API key ופרופיל שנקבעו בזמן ההפעלה. שינוי `BackendPort` בהגדרות עשוי לגרום ל־proxy לפנות לפורט חדש למרות שהמנוע עדיין בפורט הישן. שינוי API key גורם ל־proxy לקבל key חדש בזמן שהמנוע עדיין דורש את הישן, וה־Authorization מועבר אליו כפי שהתקבל.

בעיה נוספת: UI מחזיק snapshot של כל ההגדרות. Replace משמר ActiveModelFile ונתיבי external, אך אינו משמר באופן דומה aliases או פרופילים מפני snapshot ישן. autosave של העדפה פשוטה יכול להשיב נתונים ישנים שנשמרו על ידי פעולה אחרת.

המלצה: להפריד בין **הגדרות שמורות** לבין **LoadedEngineSession** קפוא הכולל PID, engine package, actual port, model identity, profile revision ו־credential פנימי. לעדכן העדפות ב־PATCH ממוקד או commands, לא להחליף את כל האובייקט על כל toggle. שינוי launch-setting יסומן Pending restart וייושם באופן מתואם.

בדיקת קבלה: שינוי theme אינו מוחק alias; שינוי key אינו שובר מנוע פעיל; שינוי backend port מבוצע רק עם restart; אין כיתוב המציג profile חדש כאילו הוא כבר פעיל.

### W04 — פריקה ובקשות משתמש אינן משתמשות באותו מנגנון תיאום — P1, סיכון מבוסס

מקור: `PublicApiProxy.cs:68,77,139`; `Program.cs` מסלולי unload/delete/alias; `EngineController.cs:132,226`.

ה־request gate שומר על בקשת inference אחת, אך unload ציבורי, unload ידני, שינוי alias ומחיקה אינם נכנסים לאותו gate. בדיקת `ActiveRequests > 0` בנקודה אחת אינה נעילה: בקשה יכולה להתחיל מיד לאחר הבדיקה. גם timer של idle יכול לראות אפס בקשות ואז לפרוק בדיוק כשהגיעה בקשה חדשה.

המלצה: coordinator יחיד ופשוט ל־load/unload/switch/restart ולשימוש ב־session. לא להוסיף עוד semaphores מפוזרים. פעולה ידנית תבחר מדיניות מפורשת: המתנה, דחייה, או ביטול בקשה באישור. idle unload יקבל אישור אטומי מול קבלת בקשה חדשה.

בדיקה: בקשה איטית מדומה + idle deadline + unload + rename + switch במקביל. אין kill במהלך בקשה אלא כשנבחר ביטול במפורש.

### W05 — kill אינו ממתין לסיום התהליך — P1, סיכון מבוסס

מקור: `EngineController.cs:234`; `BackendClient.cs:92`.

`Kill(true)` נקרא, process disposed, ומיד מוצג Unloaded. אין `WaitForExitAsync`. ניתן להתחיל את התהליך הבא לפני ש־Windows סיים את הקודם ושחרר פורט/משאבים. callback של Exited מתייחס ל־State כללי ולא לזהות generation, ולכן event מאוחר מתהליך ישן עלול לשנות מצב של session חדש.

המלצה: shutdown אסינכרוני קצר ומוגבל, המתנה ליציאה, אימות שה־PID שייך ל־session הנכון, ולאחר מכן מצב Unloaded. לשמור Job Object קיים. לא להוסיף sleep ארוך קבוע בתקווה שהוא יספיק.

### W06 — Auto context אינו הבטחת התאמה ל־VRAM — P1/P2, מוכח במקור

מקור: `EngineController.cs:180,264`.

הערכה משתמשת ב־BaseVramMiB, מקדם KV אחד לכל מודל וב־GPU הראשון מה־monitor. היא אינה מותאמת במפורש ל־Device שנבחר, dtype הנוכחי, drafter, vision workspace או concurrency. אם אין tier שמתאים, מוחזרים 32768 tokens בכל זאת, גם כשהמשקלים לבדם לא נכנסים, או כש־MaximumContext קטן מכך.

Auto גם משנה ושומר את פרופיל המשתמש בפועל. אין הפרדה מלאה בין ״ביקשתי״ ל־״נבחר בהרצה זו״. זה חשוב במיוחד בדרישת המשתמש לשמור איכות ויעילות, ולא רק להצליח להפעיל process.

המלצה: להשתמש ב־inspector ובחוזה ה־memory של המנוע כשזמין; estimator של האפליקציה יהיה estimate עם reserve מפורש ורמת ביטחון. no-fit צריך לחזור כשגיאה עם חלופות, לא tier מומצא. לבחור GPU לפי UUID/index ומאפייניו; לא רק לפי שם. Exact profile לא ישתנה בשקט. לא לשנות KV precision אוטומטית כדי להימנע מ־OOM.

בדיקה ללא GPU: מטריצת GPU fixtures, dtype, vision/drafter, model-too-large, multi-GPU ו־max-context קטן. בהמשך נדרשת מדידה אמיתית לקביעת מקדמים, לא טענה שהנוסחה הקיימת מדויקת.

### W07 — proxy קורא ומפרש את כל JSON לפני forwarding — P2, הזדמנות עם עלות מוכחת

מקור: `PublicApiProxy.cs:106–125`.

לצורך מציאת `model` נפתח buffering, כל הגוף מפורש ל־JsonDocument, מיקומו מוחזר להתחלה ואז הוא מועבר שוב למנוע. בבקשות context גדולות או תמונות base64 זה מוסיף עבודה לפני prefill: buffering, parsing, לעיתים spool לדיסק, ואז קריאה נוספת. זו אינה הוכחה לכמות האטה מסוימת, ואין להסיק מכאן שינוי ב־decode tok/s.

אין כאן הגדרה מפורשת של מגבלת body בגבול Kestrel שתואמת `MaxRequestMiB` המועבר למנוע. לכן 384 MiB בפרופיל אינו מוכיח שה־proxy יקבל גוף כזה. מגבלות השרת בפועל צריכות בדיקה ממוקדת.

המלצה בשלבים: body limit אחיד ושגיאת 413 ברורה; לקבוע תקציב buffering; לבחון streaming parser שמוציא רק model ועדיין מסוגל להעביר גוף ללא שינוי. model יכול להופיע בסוף JSON, לכן לא להניח שהוא תמיד במאות הבתים הראשונים. לא להחליף parser תקין ב־regex. אפשרות routing-header היא הרחבת חוזה, לא תחליף ששובר OpenAI clients.

בדיקה: 1 MiB/10 MiB/בקשת context ארוכה, model בסוף, escaped strings, גוף malformed, cancellation ו־SSE. למדוד TTFT proxy-only מול backend ישיר על אותם נתונים.

### W08 — תור המנוע אינו נגיש דרך ה־proxy — P2, מוכח במקור

מקור: `PublicApiProxy.cs:77`; `Contracts.cs` MaxPendingRequests/MaxConcurrency; `SettingsService.cs:92`.

Manager מאפשר בדיוק בקשה פעילה אחת ו־WaitAsync(0) מחזיר 429 מיד. בינתיים נחשפות הגדרות תור של המנוע, כגון MaxPendingRequests=50, שאינן יוצרות תור ב־Manager עצמו. זה יכול להיות נכון מבחינת VRAM, אבל UX ו־API צריכים לשקף זאת.

המלצה: לשמור concurrency=1 כברירת מחדל; לבחור תור bounded קצר עם cancellation ו־Retry-After, או להסביר בבירור שהבקשות נדחות ולא ממתינות. לא לפתוח concurrency אוטומטית כדי להציג יותר throughput; זו החלטה שמשפיעה על הזיכרון ועל המסלול במנוע.

### W09 — Logs ו־Requests מבצעים סריקה מחדש של קובץ — P2, מוכח במקור

מקור: `ManagerLog.cs:17,20`; `RequestMetricsParser.cs`; `MainWindow.xaml.cs:68,236`.

כל שורת מנוע פותחת/מוסיפה לקובץ תחת lock. Tail משתמש ב־ReadLines.TakeLast, ולכן עובר מתחילת הקובץ גם כדי להחזיר רק סוף. Requests קורא עד 10000 שורות ומפרש מחדש. קריאת Tail חולקת lock עם writer, ולכן תצוגה יכולה לעכב קליטת פלט/logging. Rotation מגביל את הקובץ לכ־5 MiB ועוד previous, ולכן זה אינו גידול בלתי מוגבל, אבל העבודה החוזרת מיותרת.

המלצה: writer אחד, buffering מתון ו־flush לפי זמן/אירוע, ring buffer לסוף הלוג, מדדים incremental. אם כבר נכתב request JSONL במנוע, להשתמש בו במקום לפרש טקסט display. להשאיר raw log לקריאה טכנית. לא לאבד שגיאות קריטיות כדי לחסוך flush.

ב־NInferEZ הקל RollingLog כבר מחזיק writer, אך עושה FlushAsync לכל שורה וקריאות כתיבה מתוזמנות fire-and-forget. אותו עיקרון של queue bounded/flush מתוזמן מועיל, עם טיפול מפורש בכשל דיסק.

### W10 — metrics אינם היסטוריית אמת מלאה — P2, מוכח במקור

ה־parser מקשר start/done לפי request ID בלבד בתוך הטקסט הנוכחי. restart יכול לאפס IDs; rotation או חיתוך Tail יכולים להסיר start ולהעלים done. חלון מציג ממוצע מחושב מהחלק שנותר, לא בהכרח ממוצע מאז פתיחת האפליקציה. סיכומי tokens משתמשים ב־int ועשויים לגלוש בעומס מצטבר.

המלצה: key של sessionId+requestId, מונים מסוג long, חלון מדידה כתוב בממשק, נתונים structured. נוסחת AverageDecode הנוכחית משקללת לפי token/time, וזה דווקא סביר; לא להחליף אותה לממוצע פשוט של rates בלי להסביר שינוי משמעות.

### W11 — polling UI עלול לחפוף ולהציג הודעות שוב — P2, סיכון מבוסס

מקור: `MainWindow.xaml.cs:68,206,443`.

DispatcherTimer מפעיל handler אסינכרוני ללא guard. אם קריאת backend נמשכת יותר משתי שניות, tick חדש יכול להתחיל לפני סיום הקודם. collection של Requests נבנית מחדש, פעולות מתרבות ו־UI עלול לקבל תשובות בסדר הפוך. כבר יש polling מותנה־מסך והפסקה ב־Tray; לשמר אותם.

PortChangedAutomatically ו־download-error עלולים להציג את אותה InfoBar בכל polling. מאחר ש־ShowInfoBar מחדש את timer הסגירה, הודעה חוזרת יכולה להיראות כאילו אינה נעלמת אחרי 10 שניות. זה מסלול אפשרי שנותר בקוד, לא קביעה שהבאג שהמשתמש דיווח עליו עדיין משוחזר.

המלצה: refresh single-flight, cancellation כשמסך מתחלף, snapshot diff במקום Clear/Add, והודעות לפי event/operation ID. לוגי progress אינם notifications חדשות.

### W12 — זיהוי GPU תלוי subprocess ללא timeout — P2, מוכח במקור

מקור: `GpuMonitor.cs:11–23`.

נקראת רק שורת GPU ראשונה, int.Parse אינו מקבל ערכי N/A, אין timeout/kill ל־nvidia-smi. אם נכשל, cachedAt לא מתעדכן ואז יכולים להיווצר ניסיונות תכופים יותר מ־10 שניות. gate עלול להיתפס זמן רב אם subprocess נתקע. זה מידע מסייע, לא סיבה שפעולת inference תמתין ללא גבול.

המלצה: timeout קצר, negative cache/backoff, parsing עם TryParse, רשימת GPUs עם UUID/compute capability, והפרדת telemetry מהחלטות launch. NVML יכול להיות שלב עתידי אם מדידה מראה צורך; אין חובה להוסיף תלות native חדשה עכשיו.

### W13 — NInferEZ הקל: Stop בזמן Starting ו־cleanup בכשל — P1/P2, מוכח במקור

מקור: `NInferEZ.Core/Services/EngineProcessManager.cs`.

Start מחזיק semaphore לאורך readiness של עד חמש דקות. Stop צריך את אותה נעילה, ולכן אינו מסוגל בעצמו לבטל Start לפני שהנעילה מתפנה. catch של Start מסמן Error אך אינו מבטיח הריגת process שנוצר. במסלול process שכבר יצא Stop חוזר בלי CleanupProcess מלא. בנוסף אין כאן אותו Job Object שיש ב־Manager.

המלצה: operation CTS שניתן לבטל בלי להמתין ל־lifecycle gate; cleanup ב־finally/מסלול כשל; Job Object; shutdown await. UI timeout אינו שקול ליציאת process.

### W14 — NInferEZ הקל: תחזוקת HTTP ידני — P2, סיכון מבוסס

מקור: `LazyInferenceApiHost.cs`; `ControlApiHost.cs`.

נעשה שימוש ב־TcpListener, parsing ידני ו־bidirectional pumps. יש מגבלות header ו־body בחלק מהמסלולים וטוקן ל־Control API, לכן אין לתאר את המימוש כנטול הגנות. אבל inference proxy מחזיק connection tasks ללא גבול אפליקטיבי ברור, אינו מגדיר deadline לקריאת header, ואינו מאמת credential לפני EnsureBackend במסלול inference. גם EngineArgumentBuilder שנבדק אינו מוסיף API key. לכן פתיחת inference host לרשת אינה מקבלת הגנת authentication רק משום של־Control API יש token. אם יתווסף key למנוע בלבד, בקשה לא מורשית עדיין תוכל לגרום לטעינה לפני הדחייה במנוע. נכפה Connection:close, כך שאין keep-alive ללקוחות.

המלצה: עבור האפליקציה הקלה, יש הצדקה לבדוק **החלפה ממוקדת של שני שירותי HTTP** ב־host מוכח, או לפחות גבולות חיבור/read-timeout ואימות לפני טעינה. להחליט לפי משקל הפצה, startup ו־RAM מדודים; לא להחליף את כל האפליקציה לצורך זה. Manager כבר משתמש ב־Kestrel, ולכן אין צורך לכתוב לו שרת חדש.

### W15 — Exact profile באפליקציה הקלה חוזר על hash גדול — P2, מוכח במקור

מקור: `MainViewModel.cs:434`; `ProfileService.cs` ValidateModelIdentityAsync.

Exact mode עם SHA מבצע hash על כל קובץ לפני Start. לאחר unload אוטומטי וטעינה חוזרת זו עשויה להיות סריקה של עשרות GB שוב. זו הגנה אמיתית, ולא קוד שצריך למחוק כדי ״להאיץ״.

המלצה: cache של אימות עם hash, זהות קובץ, גודל, mtime ומדיניות invalidation; שינוי/החלפת קובץ דורש אימות מחדש. גודל ו־mtime בלבד אינם הגנת adversarial מושלמת. Exact mode יכול לדרוש full verification מפורש, ולציין אם השתמש באימות cached. למדוד בנפרד hashing/loading/calibration, לא לכנות הכול ״טעינת מודל״.

### W16 — Working-set trim אינו חיסכון קסם ב־RAM — P3, הזדמנות

מקור: `SystemOperations.TrimWorkingSet`; `MainWindow.xaml.cs:643`; `SystemIntegration.cs`.

יש forced GC ו־SetProcessWorkingSetSize אחרי hide. הם יכולים להקטין working set מוצג בלי לבטל allocations, ולהוסיף page faults כשחוזרים ל־UI. לא הוכח כאן שהם מזיקים, אך מספר קטן ב־Task Manager אינו מספיק כהוכחת יעילות.

המלצה: להשוות private bytes, committed memory, hard faults וזמן restore לפני/אחרי. להשאיר או להסיר לפי מדידה. לא לבצע trim בזמן request פעיל כדי לשפר מדד קוסמטי.

## 5. יעילות מקום: מה באמת תופס שטח ומה ניתן לשפר

### 5.1 תמונת אחסון שנמדדה בסקירה

המספרים הבאים הם סכום אורכי קבצים ב־GiB, לא שטח פיזי מוכח ב־NTFS. hardlinks, compression ו־sparse files משנים את השטח הפיזי. אין לחבר את הטבלה כדי להבטיח נפח שיתפנה.

| תיקייה בתוך TEMP | נפח לוגי, GiB | משמעות |
|---|---:|---|
| NInferEZ Manager | 5.80 | מקור, build caches ו־dev-015/final; חלק מקבצי final מקושרים ב־hardlink |
| NInferEZ Suite/NInferEZ | 2.80 | אפליקציה קלה, תוצרים ומקורות |
| NInferEZ Suite/NInferEZ Engine | 31.89 | רובו build trees וחבילות |
| NInfer Manager WinUI | 94.34 | בעיקר models/sources/test-models ו־Full Setup ישן |
| NInfer Manager App 2 | 0.13 | בסיסים/מחקר שנשמרו |
| NInfer Manager 1.0.0 New Engine | 8.52 | חומר שנשמר בשל הקשר ל־Next |
| NInfer Manager Next | פחות מ־0.01 | PLAN בלבד |

מנוע: build-sm120a כ־6.97 GiB; build-sm86 כ־8.19; build-sm89 כ־8.06; dist כ־8.34. שלושת build trees יחד כ־23.22 GiB לוגיים. אלה מועמדים לניהול cache, **לא הבטחת מקום פנוי ולא המלצה למחוק עכשיו**.

ה־build-cache של WinUI הישן: models כ־21.22; test-models כ־22.09; sources כ־24.02; converter כ־2.98; runtime כ־0.34; hf-cli כ־0.05 GiB. כלומר כ־67.33 GiB בקטגוריות model/source לפני חבילת ההפצה המכילה מודל. חלקם משקלים מקוריים/ארטיפקטים חשובים להמשך המרה, ולכן לא להתייחס אליהם כזבל build.

Installer האחרון של Manager: 98,957,919 bytes, כ־94.37 MiB. זה אינו גודל המנוע או המודל. חבילת מנוע sm120a compressed היא 1,112,591,446 bytes לפי manifest; sm86/89 מעל 1.3 מיליארד bytes כל אחת. הבדל packaging אינו מדד איכות inference.

### S01 — hardlinks כבר קיימים; אין ״חיסכון כפול״ — P2

`fsutil hardlink list` על EXE ב־final Portable החזיר גם את הנתיב ב־dev-015. לכן שתי הרשומות הספציפיות האלה משתמשות באותו תוכן פיזי. מחיקת אחת אינה משחררת את תוכן הקובץ כל עוד השנייה נשארת.

המלצה: להמשיך להפריד namespaces של release/test, אבל לשתף רק קבצים immutable. לא hardlink ל־settings, logs, current.json או קבצי משתמש; שינוי דרך נתיב אחד משנה את התוכן בכל links. לעדכן באמצעות replacement ולא כתיבה in-place לתוצר משותף.

### S02 — Build script עדיין מעתיק payload כמה פעמים — P2

מקור: `scripts/build.ps1` ב־Manager.

מבוצעים publish ל־base, העתקה ל־Portable ול־Test Portable, ואז ZIP ו־Installer; תהליך hardlink ל־final נעשה בנפרד ולא כחלק מה־build script הבסיסי. builds עתידיים יכולים לחזור לשכפול פיזי.

המלצה: stage אחד immutable, נגזרות אריזה זמניות, Test Portable עם Data פרטי, ו־promotion מתועד. retention של dev אחרון ו־last-known-good release אחד לאחר אישור. לא להפעיל cleanup על build פעיל או test data.

### S03 — משקלים משוכפלים בין cache/historical full package — P2

יש מודל גדול גם ב־build-cache/models וגם ב־dist של Full Setup הישן. זו עדות למועמד לכפילות, לא הוכחה שכל הקבצים זהים. לא חושבו SHA מלאים על עשרות GB במסגרת הסקירה.

המלצה: פעם אחת למפות identity/hash, לשמור artifact canonical לצד מקור ופרופיל, ולקשר אליו מחקר/אפליקציות. לשמר משקלי מקור כשנדרשים למרה חוזרת. user requirement לשמור מודלים ו־Next גובר על הרצון להקטין טבלה.

### S04 — caches של המנוע ניתנים לבנייה מחדש, אך relocation שבר נתיבי cache — P2

מקור: `build-sm120a/CMakeCache.txt`, `CMAKE_HOME_DIRECTORY` עדיין מצביע לתיקייה לפני TEMP.

לשמור את שלושת ארטיפקטי המנוע וה־source זה הגיוני. לשמור כל obj/cache לנצח אינו הכרחי. מצד שני, cache קיים לאחר move אינו בהכרח build environment תקין; אין לכנות אותו ״מוכן לקומפילציה חוזרת״ ללא reconfigure.

המלצה: בפעם הבאה שמפתחים את המנוע, להגדיר build cache נקי/מעודכן מתוך המקור שהועבר, בלי לגעת בחבילות תקינות. להפריד release artifacts, dependencies cache ו־intermediates. לא למחוק חומרי Next או source conversions.

### S05 — מנועים מותקנים ו־partials ללא מדיניות retention — P2

מקור: `EnginePackageService.InstallArchive`, `InstallAsync`; `UpdateService`; `ModelDownloadService`.

גרסאות מנוע נשמרות בתתי־תיקיות; החלפה יכולה להשאיר `.previous`. הורדת מנוע cancelled/failed אינה מוחקת partial, בעוד מודל .part נשמר ל־resume. updater helpers וקבצי updates אינם מנוהלים באמצעות מדיניות cache מסודרת. לכל אחד יכולה להיות סיבה טובה להישאר; לא כולם ״מיותרים״ באותו אופן.

המלצה: קטגוריות Active / Last known good / User pinned / Download partial / Disposable; להציג נפח ואפשרות ניקוי מפורשת. partial למודל צריך identity ו־etag/revision; partial מנוע שאינו משמש resume ניתן לנקות אחרי כשל או לפי גיל. לא למחוק engine fallback לפני שחדש נטען בהצלחה.

### S06 — Self-contained כפול ו־single-file extraction — P2/P3

Manager UI מכוון net9 וה־backend net10, שניהם self-contained. UI משתמש ב־IncludeAllContentForSelfExtract. זה נוח למחשב נקי אך יכול להכפיל runtimes ולהוסיף extraction ו־antivirus scan בהפעלה קרה. ההשפעה לא נמדדה כאן.

המלצה: להשוות publish layout מסודר עם runtime בתת־תיקייה מול single-file, ולבחון יישור framework רק אם תלות WinUI מאפשרת זאת. אין להבטיח מאות MB חיסכון בלי מדידת outputs. לא לבחור framework-dependent אם זה מחייב את המשתמש להתקין דברים ידנית והורס ״פשוט עובד״.

### S07 — אין justification לדחיסה אגרסיבית של EXE GPU — P3

EXE גדול ו־cuBLAS DLL אינם סימן לקוד מת כשלעצמם. CUDA kernels, static linking ו־ספריות vendor תופסים מקום. לא להסיר DLL לפי שם או להשתמש ב־UPX כדי להקטין מספר; זה עלול לפגוע בטעינה, antivirus ובתאימות.

שיפור בר־בדיקה: להשאיר CLI/benchmarks מחוץ להפצת end-user, shared dependency cache לגרסאות מתאימות, ו־build target ממוקד. shared DLLs צריכים נעילת גרסה ואימות; לא לשתף ספריות ABI שונות רק משום ששמן דומה.

## 6. שיפורים כלליים: אבטחה, חוזים, שימושיות ותחזוקה

### G01 — Control API של Manager ללא token — P0 לפני הפצה רחבה

מקור: `Backend/Program.cs`; `WinUI/BackendClient.cs`.

השרת קשור ל־127.0.0.1, וזה גבול טוב מול מכשיר חיצוני, אבל כל תהליך מקומי יכול לפנות למסלולי settings, unload, model deletion, downloads ו־update. אין authentication פנימי, ואין בדיקת identity כש־UI מוצא health בפורט 48973 ומחליט להשתמש בשרת הקיים. כתובת loopback אינה מוכיחה שזה ה־backend של אותה הפצה.

נתיב update/download מקבל UpdateInfo מהלקוח, כולל URL, AssetName ו־SHA צפוי. SHA שמגיע מאותו לקוח אינו מאמת מקור. אין ב־UpdateService allowlist מחייב ל־repo/HTTPS/file-name, והנתיב מחובר ל־Updates באמצעות AssetName. בשילוב control לא מאומת, תהליך מקומי יכול לבקש הורדת executable אחר עם hash שהוא בחר ואז apply. הבעיה אינה הוכחת גישה ישירה מהאינטרנט; זה גבול אמון מקומי חלש, ובדפדפן/רשת יש תנאים נוספים שלא נבדקו כאן.

המלצה: token אקראי per installation/session, control port פרטי, אימות identity חוזה/גרסה/instance, ובחירת asset בצד backend מתוך manifest מאומת — לא URL שרירותי מה־UI. IPC באמצעות named pipe הוא חלופה, לא חובה. ה־NInferEZ הקל כבר כולל control-token ויש ללמוד ממנו בלי להעתיק HTTP parser ידני.

### G02 — פתיחה לרשת ללא key ו־CORS רחב — P1/P2

מקור: `PublicApiProxy.cs:61,134`; `Contracts.cs` defaults.

AllowLocalNetwork כבוי כברירת מחדל — נכון. כשהמשתמש מפעיל אותו עם ApiKey ריק, כל מי שמגיע ל־port רשאי לבצע inference ואף manager/unload. bind ל־0.0.0.0 כולל ממשקים מרובים; הוא אינו whitelist של LAN או Tailscale. CORS=* מופעל כברירת מחדל ומאפשר origins רחבים להשתמש ב־API ללא key, בכפוף להגבלות דפדפן נוספות.

המלצה: לפני LAN להציע/לחייב key או אישור סיכון מפורש, להציג listen scope, firewall ו־Tailscale permissions, ולהגביל CORS לפי צורך. האפליקציה לא צריכה לשנות firewall או Tailscale בלי אישור. מידע health שאינו מאומת יוגבל למה שנחוץ.

### G03 — עדכון Portable אינו transactional — P1

מקור: `UpdateService.cs:67–85`.

ה־runner ממתין עד 120 שניות ל־PIDs אך מתעלם מה־bool של WaitForExit ומהחריגות. אחר כך הוא מוחק Backend ו־Docs לפני שסיים העתקה, ומעתיק root files. אין backup/recovery לאפליקציה הקודמת אם copy נכשל, אם antivirus נועל קובץ או אם החשמל נופל. ההגנה שלא להעתיק Data/Models/Engine חשובה וקיימת; היא אינה rollback.

המלצה: staging מלא, validation של package layout, סגירה מאומתת, swap/replacement מתועד ו־last-known-good. במצב Windows שבו root exe לא ניתן להחלפה אטומית אחת, להשתמש ב־journal של צעדי commit/restore. אחרי launch מוצלח אפשר לנקות את הגרסה הישנה כפי שהמשתמש ביקש. לא למחוק אותה לפני שההחלפה הושלמה.

אין צורך במערכת updater ענקית; הרכיב הזה לבדו מצדיק כתיבה מחדש ממוקדת.

### G04 — חוזה engine channel אינו בודק מספיק identifiers — P1/P2

מקור: `EnginePackageService.Validate`, `RelativeRoot`, `InstallArchive`, `IsInstalled`.

יש אימות HTTPS, host/repository, hash, size ו־zip traversal. אך engineVersion ו־cudaArchitecture שמרכיבים נתיב destination אינם מאומתים כמזהים בטוחים או architecture מהקבוצה המותרת. חבילת channel תקינה מבחינת checks הקיימים יכולה להכיל component נתיב לא תקין. בדיקת staging zip אינה מגינה אוטומטית על destinationRoot שנבנה מ־manifest identifiers.

הסיכון תלוי feed/cache לא אמין או פגיעה בשרשרת ההפצה; אין ראיה שה־feed הנוכחי זדוני. המלצה: whitelist architecture, grammar של version, containment בכל יעד, uniqueness packages, ותקרת channel size. IsInstalled לא יסתפק ב־EXE exists אלא יבדוק manifest/required runtime files וחוזה. לא לעשות hash של כל runtime בכל tick.

### G05 — התקנת מנוע מתחרה עם model load — P1/P2

Program דוחה Install כשמנוע כבר Loading/Ready, אבל לאחר שאושרה הורדה לא נשמר מצב global המונע lazy inference חדש במהלך ההורדה/activation. המשתמש יכול לקבל בקשה ולהתחיל מודל בזמן שחבילת מנוע חדשה מתחילה activation. pointer חדש אינו משנה process שכבר רץ, והתצוגה עלולה להציג active package שונה מה־loaded package.

המלצה: download מותר ברקע, install/activate מתבצעים כ־transaction מול lifecycle coordinator. להפריד Downloaded / Installed / Selected / Running ולציין pending switch. `.previous` אינו last-known-good עד שבוצעה בדיקת launch מתאימה.

### G06 — זיהוי מודל לפי גודל בלבד; `.ninfer` אינו הוכחת יכולות — P1/P2

מקור: `ModelCatalog.cs:92–108,190`; מול `Engine/apps/inspect/main.cpp`.

LinkExternal מנסה filename+size, ואז כל entry בעל אותו size. שני artifacts שונים באותו גודל יכולים לקבל אותו פרופיל, ID ויכולות. local artifact לא מוכר מקבל defaults של NInfer v3, native/max context ו־vision=false בלי לקרוא metadata. עקב כך DFlash2 או vision שקיימים בפועל לא בהכרח מזוהים. בדיקה מיוחדת למודל IQ3 נעשית לפי ModelId קשיח, לא לפי capabilities של artifact.

המלצה: inspection מקומי קצר באמצעות המנוע שכבר קיים; identity מה־artifact, path/key נפרדים, hash כשנדרש. presets יתבססו על format/components/capabilities וגרסת engine, לא על גודל בלבד. fallback unknown יהיה זהיר ומוצהר, ללא הבטחה שכל מודל נתמך. אין צורך להמיר או לכמת מחדש את המודלים לשם כך.

### G07 — מודל שנמצא בתוך Models מסומן כ־external — P2, מוכח במקור

מקור: `RefreshLocalArtifacts` קורא LinkExternal לכל file; מול delete ב־Program והטקסט ב־MainWindow.

סריקת Models כותבת את הנתיב ל־ExternalModelPaths. לכן גם קובץ בתוך ספריית האפליקציה יכול להיחשב linked external. UI מבטיח שקובץ פנימי יועבר ל־Recycle Bin, אבל branch external רק unlinks. רענון הבא יגלה אותו שוב. זו אינה תקלה מסוכנת של מחיקה מיותרת; זו התנהגות לא עקבית ואפשרות שמודל ״מחוק״ חוזר לרשימה.

המלצה: מקור LocalManaged/ExternalLinked מפורש; discovery אינו Link command ואינו צריך לכתוב settings לכל file. path relative בתוך Models משפר Portable relocation. מחיקת external תמיד unlink; managed delete דורש אישור.

### G08 — צדדי פרופיל שונים בין האפליקציות — P2

NInferEZ הקל יודע `.ninferez.json`, strict parsing, engine requirements ו־Exact/Custom/Automatic, וגם inspection. ב־Manager הנוכחי לא נמצאה קריאה של sidecar זה במקורות; הוא משתמש בפרופילים משלו לפי file key. לכן ״מבוסס על אותו מנוע״ אינו אומר ״שתי האפליקציות מפרשות אותו preset״.

יש גם הבדל מפורש ב־KV vocabulary: enum של Manager כולל K8v4, המנוע הנוכחי מקבל k8v4, בעוד validator של האפליקציה הקלה דוחה אותו בבדיקה קיימת. לא להסיק שכל build מאפשר כל dtype; צריך capability-aware contract ולהסביר מדוע פורמט מותר/אסור ב־target מסוים.

המלצה: מודל domain קטן משותף לפרופיל/capabilities, עם adapters לשני ה־UIs. לבדוק requiredFeatures מול engine וגם artifact. Exact mode ישמור משמעות מדויקת, ולא לשנות defaults תוך ״איחוד״. אין טעם לשתף XAML/WPF או לכפות UI אחד על השני.

### G09 — Settings ללא schema/migration מסודרים — P2

מקור: `Contracts.cs`; `SettingsService.Load/Validate`; מול תוכנית `NEXT-VERSION-0.0.1-PLAN.md`, סעיף 11.

התוכנית דורשת schemaVersion, migration ו־last-known-good engine, אך settings הנוכחי אינו כולל את כולם. Load אינו מפעיל Validate; catch מחזיר defaults בשקט גם בקובץ פגום. Validate בודק חלק מהטווחים אבל לא את כל דגלי engine, enum values, null profiles, relation בין spec/proposal ו־ports מול Control port. שגיאה יכולה להגיע למנוע באיחור.

המלצה: schemaVersion ומיגרציות מפורשות, שמירת backup קובץ פגום והודעה שאינה מוחקת מידע, normalization של dictionaries וה־comparers, profile validators לפי capabilities. Apply/Save לא יחייבו קלט שרירותי למנוע כדי לגלות שהוא לא נתמך. אין צורך ORM או database בשביל settings קטנים.

### G10 — חוזה API רחב יותר מהטיפול בפועל — P2

כל `/v1` מלבד models נכנס למסלול model-selection ו־load. מסלולים כגון GET/DELETE של response קיים אינם בהכרח inference חדש; model query/body יכולים להיות שונים. Public CORS מפרסם GET/POST/OPTIONS בלבד, אף שמנוע יכול לתמוך בפעולות נוספות. JSON שאינו object עשוי להגיע ל־TryGetProperty ולהחזיר 503 במקום שגיאת 400.

המלצה: מיפוי routes מוצהר עם סיווג metadata/inference/session-operation. לשמר endpoints נתמכים ולא להכריז ״API מלא״ בלי מטריצת תאימות מול גרסת המנוע. SSE cancellation ואירועים חייבים בדיקה. aliases: להגדיר case/whitespace policy אחידה ולוודא שהשם שנפתר ב־Manager תואם למה שמגיע למנוע.

### G11 — restart API אינו מחזיר listener קודם במקרה כשל — P2

מקור: `PublicApiProxy.RestartAsync`, `PortService`; `MainWindow.SaveRestartPort_Click`.

השרת הישן נעצר לפני ניסיון start של החדש. UI אף שומר settings לפני restart. אם bind החדש נכשל, API נשאר כבוי וה־settings כבר מכוונים ליעד שנכשל. IsAvailable בודק loopback אך actual LAN bind נעשה על Any; בדיקת זמינות מקדימה גם אינה reservation ומועדת ל־TOCTOU.

המלצה: config transaction, ניסיון bind אמיתי וטיפול ברור/rollback. במעבר לאותו port עם scope אחר יש צורך stop/start אבל עדיין אפשר לשחזר את הקודם. לא להסתפק בהוספת retry בלי לסדר מצב שמור מול פעיל.

### G12 — Diagnostics השחרה חלקית — P2

מקור: `SystemOperations.cs`.

API key ו־external paths נמחקים מה־settings; paths של template/log/preset מושחרים. אלה הגנות קיימות. אבל לוגים יכולים לכלול paths אחרים, aliases, שמות קבצים, ופקודות/שגיאות שאינן נלכדות ב־regex הנוכחי. regex הדורש `:` או `=` אינו כיסוי מלא ל־`--api-key value`. אין לסמן diagnostics כ־״ללא מידע אישי״ על סמך כמה Replace.

המלצה: sanitization לפי field, רשימת סוגי מידע שמוסרים, preview לפני sharing, ומבחני regression עם drive paths, tokens ו־Unicode. package privacy audit הוא שכבה נוספת, לא הוכחה משפטית/אבטחתית מלאה. Test Portable המכיל Data של המשתמש הוא תוצר מקומי בלבד ואסור להעלות אותו כחבילת clean public.

### G13 — MainWindow ו־MainViewModel מרכזים יותר מדי אחריות — P2

Manager MainWindow מכיל lifecycle UI, navigation, responsive layout, models, settings editor reflection, update wizard, download progress, tray ו־notifications. NInferEZ MainViewModel גדול עוד יותר. מספר שורות אינו כשלעצמו פגם, אך הקשר בין responsibilities מקשה על בדיקות ועל איתור שגיאות כגון stale settings.

המלצה: להוציא בהדרגה SettingsEditor, Notifications, ModelLibrary presentation, EngineLibrary presentation ו־Update workflow; להפריד commands/state מ־Windows controls. לשמר XAML ו־design language שאושרו. לעבור ל־MVVM חלקי רק היכן שזה מפשט — לא framework/refactor כולל לשם סגנון.

Reflection לכל property writable הוא פתרון מהיר, אבל חושף גם no-op ואינו נותן הסבר, יחידות, dependency או capabilities. עדיף descriptor קטן עם type/range/units/restart requirement ו־visibility. זו רשימת הגדרות מוצר, לא מערכת plugin כללית.

### G14 — tasks ו־cancellation אינם מנוהלים אחיד — P2

יש fire-and-forget ב־idle timer, update download, install, log writers, refresh, ו־activation pipe listener. בחלק מהם catch טוב; בחלקם פעולה שלא מתחילה/מבטלת לפני try יכולה לא להשלים status, או dispose עלול להתחרות במשימה חיה. Update download אין gate/cancel מקבילים למודל, ו־HttpClient בלי timeout משאיר UI polling בלי deadline אם remote תקוע.

המלצה: operation ownership ברור, cancellation token של app lifetime, חיבור משימות ברקע לתוצאה, cancel/dispose/await בסדר קבוע. לא ליצור generalized job framework לכל לחיצה; שלושה operation types פשוטים מספיקים.

OperationStatus ממשיך לחשב elapsed מ־StartedAt גם אחרי Completed/Failed/Cancelled, ולכן זמן פעולה סופית גדל לאחר שסיימה. לשמור elapsed קפוא לסיום ולהשתמש ב־Stopwatch למדידת משך; DateTime נשאר timestamp.

### G15 — זיהוי גרסאות ועדכונים חלקי — P2

UpdateService משתמש ב־System.Version וב־latest release בלבד. תג pre-release כמו `0.0.2-preview.1` אינו מתקבל באותו מסלול. Engine channel מציג חבילות גם Preview/Stable בלי מדיניות ערוץ מלאה ב־settings. שמירת version constant בכמה projects/scripts תקינה כרגע לצורך קו 0.0.1, אבל דורשת מקור אמת אחד לקראת release.

המלצה: SemVer parser קטן/מוכח, app channel ו־engine channel מפורשים, fallback cache, ETag/If-None-Match, timeout ו־retry מתון. לא לבדוק remote באופן אינטנסיבי. השוואת release צריכה לכלול build provenance ולא רק מספר version שהמשתמש ביקש לשמור קבוע בשלב dev.

### G16 — provenance של build לא מספיק קשיח — P2

Build script קורא git rev-parse אך אינו מכשיל build אם Git נכשל, ואינו מתעד dirty tree או snapshot hash. final metadata מתאר sourceBuild אך אינו חבילת attestation. לכן hash של exe מוכיח שזה אותו קובץ, לא מאיזה מקור בדיוק נבנה.

המלצה: גרסת מוצר 0.0.1 יכולה להישאר, אך metadata חייב לכלול BuildId, commit, dirty/source digest, SDK/compiler/CUDA, engine package hash וה־profile/catalog revision. לא לעדכן safe.directory גלובלי כדי לעקוף בעיה; לסדר ownership או להיכשל באופן ברור במסלול release.

### G17 — הנחיות engine מקומיות סותרות את סביבת ההפצה — P2

`NInferEZ Engine/AGENTS.md` כולל הנחיות inherited על RTX 3090/Linux/paths שאינם תואמים למבנה Windows/TEMP. CMake comments גם משקפים שכבות history. לשמר upstream attribution, אך להפריד הנחיות contributor עדכניות מהיסטוריה. אחרת כלי או מפתח עלולים להריץ build/test לא רלוונטי.

זה תיקון תיעוד/מבנה, לא סיבה לשנות kernels. הנחיות לא להריץ GPU בלי אישור ומשאבים נשארות חשובות.

## 7. קוד מת, כפילויות וחלקים היסטוריים

### D01 — שדה UI שנכתב ואינו נקרא — ניקוי בטוח קטן

ב־Manager `_availableUpdate` מוגדר ב־MainWindow ונכתב אחרי CheckUpdate; בחיפוש המקור לא נמצאה קריאה שלו. ניתן להסירו אחרי build/regression קטן, או להשתמש בו בכוונה אם יש UI state עתידי. אין לצפות לחיסכון RAM או CPU מהותי.

### D02 — מאפיין הגדרה ללא אפקט במנוע — לא למחוק בלי migration

`MaxCacheMarkersPerRequest` נשמר ב־Contracts ונוצר לו editor advanced, אך BuildArguments אינו משתמש בו בשל הערה inherited על NInfer 0.8.0. **במנוע שלנו כיום serve_options.cpp כן כולל `--max-cache-markers-per-request`**. זו אי־התאמה בין adapter למנוע, לא סתם field מיותר.

המלצה: capability-aware exposure. אם supported, להעביר ולאמת; אם deprecated בגרסה מסוימת, להסתיר/לסמן unsupported. לשמר קריאת settings ישנים. ניקוי שדה זה בלי בדיקה יכול להסיר שליטה שהמנוע דווקא תומך בה.

### D03 — מתודה ו־properties ללא צרכן UI שנמצא

`BackendClient.ImportAsync` לא נקרא מ־MainWindow הנוכחי. `ModelItemView.ImportVisibility` אינו מופיע ב־binding במקורות הממשק שנבדקו. אלה מועמדים לניקוי adapter/view-model, לא ראיה שאפשר למחוק endpoint import או שירות backend: הם חלק מה־Control contract ויכולים להיות שימושיים ללקוחות אחרים.

לגבי יתר public properties, אין להסיק קוד מת רק כי IDE לא מוצא references: XAML binding/reflection/serialization יכולים לצרוך אותם. חיפוש references + build + contract test נדרשים לכל הסרה.

### D04 — כפילות אמיתית בין ה־Manager החדש לישן

בוצעה השוואת SHA של קובצי `.cs/.xaml/.csproj` באותם paths, ללא bin/obj. נמצאו 25 זוגות, מהם שישה זהים byte-for-byte: GpuMonitor, ManagerLog, PortService, ProcessJob, RequestMetricsParser ו־Contracts csproj. שאר הזוגות שונים, ולכן אין לטעון שכל האפליקציה היא עותק זהה.

המלצה: אם הישן נשמר כמוצר frozen, לא חייבים shared library עכשיו. אם שני המוצרים יקבלו תיקונים מתמשכים, ספריית host services קטנה יכולה למנוע תיקון באחד והשארת bug בשני. אין למזג state/API/UI של המוצרים בכוח; יש להם זהות ו־contracts שונים.

### D05 — Setup של NInferEZ הקל אינו קוד מת

למרות שאין EXE Setup נוסף בתוך Portable, `scripts/package.ps1` עדיין בונה את פרויקט NInferEZ.Setup לצורך Installer וגם uninstall. לכן אין למחוק אותו על בסיס דרישת המשתמש ״בלי Setup נוסף ב־Portable״. המוצר צריך את שתי ההפצות.

### D06 — Next ומחקר אינם ״קוד מת של Manager״

Next PLAN, Offload research, Performance Lab והמנועים הנלווים אינם רצים אוטומטית במוצר האחרון. הם יכולים להיות reference/archive ולהישאר מחוץ ל־build graph. המשתמש ביקש לשמור אותם; אין הצדקה להסירם בסקירה זו. עדיף README קצר לכל תיקייה: status, source, שימוש והאם runnable.

### D07 — קוד converter אינו חלק מ־startup inference

תיעוד המנוע מציין שינוי קבלה של `ssm_alpha.weight` ב־F32 והמרת projection קטן ל־BF16, בלי לשנות quantized GGUF block matrices. קוד ההמרה הוא חלק מ־toolchain/תמיכת פורמט, לא מטען runtime שיש למחוק כדי לשפר tok/s. רישום ארטיפקט צריך לשמר provenance של ההמרה כדי שלא נחליף בטעות IQ3 בפרופיל/משקל אחר.

## 8. המנוע: שיפור ביקורתי בלי לפגוע באיכות

### 8.1 אין הצדקה לכתיבה מחדש של kernels

ה־fork המקומי מכוון להיות דק: התאמות MSVC, build targets, inspection/contract והמרת tensor קטנה. אין ראיה מהסקירה שהחלפת המנוע תשפר את המדדים שנמדדו בעבר. שינוי format, KV, speculation, rope או math path הוא שינוי שעשוי להשפיע על תוצאה, לא refactor קוסמטי.

לכן העדיפות היא לשפר את client adapter לפני CUDA. לא לבטל graphs/prefix cache/DFLASH רק כדי לפשט קוד. לא לכמת מחדש משקלים כדי לחסוך קצת מקום בחבילת app.

### 8.2 אפשרויות build קיימות אינן הבטחת שיפור

ב־sm120a CMakeCache שנבדק: `NINFER_SM120_NATIVE=OFF`, `NINFER_PDL=OFF`, `NINFER_NVCC_SPLIT_COMPILE` ריק. CMake עצמו מסביר שגם במסלול compatibility נשמרים units ייעודיים ל־NVFP4/FP8 על sm120a. לכן ״native OFF״ אינו מוכיח ש־NVFP4 לא מקבל נתיב מתאים.

אפשר בעתיד לבדוק native routes/PDL כ־build ניסויי נפרד, ולא לשנות release הנוכחי. נדרשים אותו artifact, אותו preset, prefill/decode/TTFT/VRAM ו־correctness/perplexity/retrieval. split-compile הוא בעיקר יעילות **בנייה**, לא מהירות inference; גם לו אין להפעיל 0 עם Ninja parallel גבוה בלי לבדוק RAM/CPU של קומפילציה.

### 8.3 כיסוי GPU שונה מכיסוי build

הפצות sm86/sm89/sm120a עם manifests קיימות. compile/contract success אינו הוכחת Stable לכל 3000/4000/5000 או כל מודל. זיהוי substring בשם GPU שימושי כהמלצה ראשונית, לא כתנאי סופי. compute capability, driver, dtype, model components ו־VRAM נדרשים יחד.

המשך נכון: מדיניות qualification לפי target ומודל, public preview ברור, diagnostics בטוחים לדיווח קהילה, וחוזה capabilities. אין צורך fork חדש לכל דגם אם אותה architecture ונתיבי runtime מתאימים.

## 9. פערי בדיקות והמלצות לאימות יעיל

### 9.1 מה קיים ומה אינו מוכח

בדיקות Manager הנוכחיות מכסות יצירת arguments, presets ידועים, local aliases, link/import verification, stale ActiveModel preservation, models/health לפני load ו־LAN bind. הן אינן מכסות actual download commit, update rollback, readiness עם API key, restart races או lifecycle תחת בקשות פעולות.

בדיקות האפליקציה הקלה רחבות יותר בחוזה preset, engine archive ו־control auth, ויש בדיקת lazy proxy עם backend מדומה. זה עדיין אינו test לכל error/cancel/concurrent route או הבטחת HTTP מלאה.

Build/package/hash/privacy checks אינם תחליף למבחני workflow. מצד שני, לא חייבים להפעיל מודל של 27B כדי למצוא bugs של file-sharing ו־state.

### 9.2 חבילת בדיקות host-only מומלצת תחילה

1. שרת הורדות מקומי: full, Range, server ignores Range, 206 עם offset לא מתאים, 416 כאשר partial כבר מלא, hash mismatch, network drop, cancel/resume ו־commit.
2. updater: חתימה/hash/source policy, zip חסר, target לא נכון, process לא יוצא, locked file, disk-full, failure באמצע copy, rollback והגנת Data/Models/Engine.
3. fake engine process: delayed ready, 401, crash לפני ready, cancel באמצע, unload במהלך request, exit event מאוחר ותהליך יתום.
4. settings: schema migration, corrupted JSON, null/invalid enum, שני writers, stale UI, alias/profile persistence, Device לא קיים וטווחים לפי capabilities.
5. proxy: malformed/array JSON, unknown model, large body, body limit, query routes, GET/DELETE responses, SSE, cancelled client ותור bounded.
6. library: same-size different artifacts, internal/external origin, rename, file moved, two models sharing basename, discovery שאינו כותב settings בכל refresh.
7. notifications: repeat progress/error לא מאפס dismissal; hover מעבר ל־10 שניות; no duplicate tick; restart state final elapsed קבוע.
8. release audit: clean Installer/Portable לעומת Test Portable מקומי; מקור build מזוהה; קבצי secrets/path fixtures; dependencies/licenses קיימים.

### 9.3 בדיקות GPU רק בשלב מתאים ובאישור

אחרי שה־host workflows תקינים: השוואה בין engine ישיר ל־Manager עם **אותו binary ואותו effective preset**. להפריד cold/warm start, hash, calibration, prefill, decode, cache reuse ו־context אמיתי. להגדיר מקום לתשובה ו־n_past אמיתי במדידה. לא לפרש window configured=512K כהוכחה שפענוח נמדד ב־n_past≈510K.

למדוד API latency overhead ומנוחה ב־Tray, ולהשוות תוצאות/איכות לפני ואחרי refactor. לא לערבב שינויי KV/DFLASH עם שינוי proxy ואז לייחס הבדל ל־UI.

## 10. האם לבנות מחדש? החלטה לפי רכיב

| רכיב | המלצה | הסיבה |
|---|---|---|
| CUDA engine/פורמט משקלים | לשמר | אין ראיה המצדיקה סיכון של rewrite; בסיס upstream עובד ועתיר ידע |
| UI/שפת עיצוב שאושרה | לשמר | בעיות lifecycle/IO אינן דורשות עיצוב מחדש או מעבר Web/Electron |
| EngineController/coordinator | refactor ממוקד | session immutable, cancellation ו־atomic lifecycle פותרים כמה תקלות ביחד |
| Portable updater | לבנות מחדש את הרכיב הקטן | destructive copy בלי rollback; patch של catch אינו מספק |
| Model/Update download IO | לתקן scopes, לא rewrite כולל | lifetime מוגדר היטב; אפשר להוסיף tests קטנים ומהירים |
| Profile/model identity | לשתף domain קטן | inspector ו־strict preset כבר קיימים; למנוע שתי אמיתות |
| Manager HTTP proxy | לשמר Kestrel, לשפר routes/buffering | אין הצדקה להחלפת host; צריך contract וגבולות נכונים |
| Lightweight HTTP ידני | לשקול החלפה חלקית אחרי מדידה | חיסכון בקוד protocol/timeout/security מול משקל ASP.NET |
| MainWindow/MainViewModel | לפצל בהדרגה | testability ותחזוקה בלי לשבור תצוגה מוכרת |
| מערכת האחסון/build | לסדר retention/promotion | משקל גדול הוא artifacts/weights, לא source |
| האפליקציה הישנה | frozen + תיקונים ממוקדים אם נחוצים | לשמור מוצר, לא להפוך אותו לפרויקט שכתוב נוסף |
| Next | לשמר בנפרד | זו תוכנית/מחקר נפרד שנדרש במפורש לשמור |

כתיבה מחדש מלאה תגדיל זמן וסיכון, תחזור לבעיות UX/packaging שכבר נפתרו, ולא תבטיח שיפור inference. refactor נקודתי נותן ערך גבוה יותר. שינוי technology של UI מוצדק רק אם קיימת דרישת מוצר שלא ניתן לענות עליה היטב בבסיס הנוכחי; לא נמצאה דרישה כזו בסקירה הזאת.

## 11. סדר עבודה מומלץ, בלי לבצע עכשיו

### שלב A — לחסום תקלות ומסלולים לא בטוחים

Control token + asset source policy; readiness authenticated; תיקון scopes של הורדות; downloader integration tests; Portable updater transactional; שמירת user data. זה קוד קטן יחסית אך בעל השפעה גבוהה. לא לערב בשלב זה שינוי preset או CUDA.

### שלב B — סדר במצב המנוע וההגדרות

LoadedEngineSession, coordinator אחד, await shutdown, immutable launch snapshot, settings PATCH/migration, configured-versus-effective UI. בדיקות fake engine במקום GPU. לשמור aliases, port 8173, idle default שלוש דקות, פרופילים ומשקלים.

### שלב C — זהות מודל ויכולות אמת

להשתמש ב־inspector הקיים, internal/external origin, exact preset ו־sidecars, engine-capability adapter, no-fit context תקין ו־multi-GPU. לא לטעון מודל רק כדי להציג רשימה.

### שלב D — efficiency מדודה

single-flight polling, event notifications, incremental logs/metrics, hash cache, body-routing optimization. לקבוע baseline לפני שינויים; לבצע שינוי אחד בכל פעם במקום ״אופטימיזציה״ משולבת שאי אפשר לייחס לה תוצאה.

### שלב E — אחסון והפצה

retention של build caches, artifact canonical, clean/test separation, consistent provenance ו־release audit. ניקוי רק אחרי inventory, זהות קבצים והסכמת המשתמש. לא למחוק מודלים/מקורות/Next. לא ליצור ZIP/portable נוסף רק כדי להוכיח תיקון קוד לפני שנדרשת חבילת בדיקה.

### תנאי הצלחה

- אותם artifact bytes ואותה מדיניות inference בפרופיל Exact.
- API זמין בלי model load, ומודל נטען רק בעת צורך מפורש.
- ביטול/פריקה/החלפה לא משאירים process ולא קוטעים request ללא מדיניות ברורה.
- Settings נשמרים בלי לבטל alias/profile חדשים ובלי להציג תצורה שאינה פעילה.
- download/update עובדים על Windows עם אימות ו־rollback, לא רק compile success.
- clean packages ללא Data אישי; test copy המקומי נשאר מבודד.
- מקום שנחסך נמדד פיזית, לא רק דרך סכום file lengths.
- אין רגרסיה נמדדת ב־TTFT/decode/VRAM/איכות לעומת engine ישיר באותו preset.

## 12. מפתח מקורות מקומי להמשך עבודה

כל הנתיבים יחסיים לתיקיות שב־TEMP. מספרי שורות מתייחסים למצב המקור ביום הסקירה, ויכולים להשתנות לאחר תיקון.

**Manager:**

- `src/NInferManager.Backend/EngineController.cs`: launch arguments, readiness, cancellation, context estimate, process lifetime.
- `src/NInferManager.Backend/PublicApiProxy.cs`: lazy routing, auth, buffering, streaming, request gate, idle unload.
- `src/NInferManager.Backend/Program.cs`: Control routes, update apply, model/engine mutations.
- `src/NInferManager.Backend/ModelDownloadService.cs`: download/resume/hash/commit.
- `src/NInferManager.Backend/UpdateService.cs`: release check/download ו־PortableUpdateRunner.
- `src/NInferManager.Backend/EnginePackageService.cs`: channel validation, install, pointer, target recommendation.
- `src/NInferManager.Backend/ModelCatalog.cs`: discovery, identity, feeds ו־profile defaults.
- `src/NInferManager.Backend/SettingsService.cs`: persistence/validation/full replacement.
- `src/NInferManager.Backend/GpuMonitor.cs`, `ManagerLog.cs`, `RequestMetricsParser.cs`, `SystemOperations.cs`, `AppPaths.cs`.
- `src/NInferManager.Contracts/Contracts.cs`: settings/profile/API contracts.
- `src/NInferManager.WinUI/MainWindow.xaml.cs`, `BackendClient.cs`, `App.xaml.cs`, `WindowIcon.cs`, `TrayIconController.cs`.
- `tests/NInferManager.Backend.Tests/Program.cs`.
- `scripts/build.ps1`, `scripts/audit-release.ps1`, `installer/NInferEZManager.iss`.
- `artifacts/0.0.1/final/build-info.json`, `docs/NEXT-VERSION-0.0.1-PLAN.md`.

**NInferEZ הקל:**

- `src/NInferEZ.Core/Services/EngineProcessManager.cs`, `LazyInferenceApiHost.cs`, `ControlApiHost.cs`.
- `ProfileService.cs`, `ArtifactInspector.cs`, `EngineArgumentBuilder.cs`, `ModelLibraryService.cs`, `RollingLog.cs`.
- `src/NInferEZ.App/MainViewModel.cs`; `src/NInferEZ.Setup`; `scripts/package.ps1`; `tests/NInferEZ.Tests/Program.cs`.

**Engine:**

- `UPSTREAM.md`, `AGENTS.md`, `CMakeLists.txt`, `scripts/build.ps1`, `scripts/package-release.ps1`.
- `apps/inspect/main.cpp`, `apps/serve/main.cpp`, `src/serve/http_server.cpp`, `src/serve/serve_options.cpp`.
- `build-sm120a/CMakeCache.txt`, `dist/release-manifest-sm120a.json` ומקביליהם לשני targets האחרים.

**ישן/Next:**

- `NInfer Manager WinUI/src`, `build-cache`, `dist/NInfer-Manager-1.1.0-Full-Setup`.
- `NInfer Manager Next/PLAN.md`, תיקיות המחקר שנשמרו בנפרד.

---

סיכום: יש ערך משמעותי לייצוב ולארגון ממוקדים, במיוחד בגבולות app↔engine, IO ועדכון. אין בסיס להבטיח כעת האצת tok/s במספר מסוים או לטעון שנדרש rewrite מלא. שיפור אמינות וביטול עבודה מיותרת הוא יעד מוצדק; שינוי איכות/כימות/נתיבי CUDA דורש הצדקה ובדיקות נפרדות.
