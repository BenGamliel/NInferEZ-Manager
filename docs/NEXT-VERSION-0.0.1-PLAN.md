# NInferEZ Manager 0.0.1 — Product and Development Plan

מסמך זה הוא תוכנית המוצר ומסמך המעקב החי של NInferEZ Manager 0.0.1. הוא מתעד גם את היעד
וגם את מצב המימוש בפועל; סעיף 19 הוא מקור האמת להתקדמות, לבדיקות ולשלבים שעדיין פתוחים.

## 1. מטרת המוצר

NInferEZ Manager נועד להנגיש את NInfer למשתמשי קצה שאינם רוצים לנהל ידנית קבצי מודל, ארגומנטים, מנועים, פורטים או מגבלות VRAM.

ברירת המחדל צריכה להיות:

1. האפליקציה מזהה את החומרה.
2. מציעה את המנוע המתאים ביותר שניתן לזהות בבטחה.
3. מציגה מודלים מומלצים שניתנים להורדה ישירות.
4. בוחרת למודל פרופיל מהיר ובטוח בהתאם לחומרה.
5. מאפשרת שימוש מיידי דרך הממשק ודרך API.

משתמש מתקדם יוכל לשנות מנוע, פרופיל, context והגדרות נוספות, אך האפשרויות המתקדמות לא יעמיסו על מסלול השימוש הרגיל.

### 1.1 חתימת המותג המשנית

- לוגו NInferEZ נשאר הלוגו הראשי בכל מצב.
- לוגו `2beng2` ישמש רק כחתימת `BY` קטנה באזורים משניים מתאימים, כגון דף Engine,
  תיעוד המנוע ועמודי release; הוא לא יחליף icon, splash או סימן מוצר ראשי.
- שילוב `BY` ולוגו `2beng2` באזור התחתון של התפריט הצדדי הפתוח הוא שלב UX נפרד.
  לפני החלתו בכל החבילות תיבנה גרסת בדיקה אחת ותוצג למשתמש לאישור חזותי.
- במצב sidebar מצומצם אין להציג את החתימה המלאה; אם תאושר גרסה קומפקטית היא תהיה
  נגישה בריחוף ולא תפגע באזור מצב ה־GPU או בכפתור הרחבת התפריט.

## 2. גבולות הגרסה

כלול בגרסה זו:

- הרחבת אפליקציית NInferEZ Manager בלבד.
- קטלוג מודלים מומלצים שמתעדכן מרחוק.
- הורדת מודלים מתוך האפליקציה.
- פרופיל אוטומטי לפי המודל והחומרה.
- ספריית מנועים מקומית עם הורדה, עדכון, מעבר ו־rollback.
- קטלוג מנועים מרוחק מ־GitHub.
- תצוגת עדכונים ומודלים חדשים.
- ניהול המנועים, התאמת הכרטיס והעדכונים בתוך דף `Engine` הקיים.
- דף `Settings` הקיים ללא טאבים, עם שמירה אוטומטית והסרת הכפתור הכללי `Save changes`.
- עדכון עצמי של האפליקציה מ־GitHub Releases, כולל `Check for updates`, התקנה אטומית
  וניקוי גרסת האפליקציה הישנה לאחר אימות הגרסה החדשה.
- טעינת מנוע ארוכה בצורה אסינכרונית, עם שלב נוכחי, זמן שחלף, טבעת התקדמות וביטול.
- Installer, Portable ו־Test Portable לפי המבנה הקיים.
- בדיקות backend, packaging וממשק ללא צורך ב־GPU, ובדיקות חומרה ממוקדות במקום שבו הן באמת נחוצות.

לא כלול בגרסה זו:

- פיתוח או קומפילציה של מנועים חדשים לכרטיסים נוספים.
- שינוי פורמט NInfer או המרת מודלים.
- העלאה של קבצי מודל או מנוע חדשים ל־GitHub/Hugging Face.
- הבטחת ביצועים על חומרה שלא נבדקה בפועל.
- חשיפת ה־API לרשת המקומית כברירת מחדל.

## 3. מצב קיים

### 3.1 יכולות שכבר קיימות

- אפליקציית Windows עם ממשק מודרני, tray ו־single instance.
- API מקומי שעולה אוטומטית בפורט 8173.
- טעינת מודל רק בבקשה הראשונה ופריקת VRAM אוטומטית לאחר זמן מוגדר.
- תיקיית `Models`, סריקת מודלים מקומיים וקישור קובץ ממיקום חיצוני.
- שם תצוגה/alias שנשמר ומשמש גם ב־API.
- פרופילים, הגדרות context, הגדרות speculative ו־KV cache.
- לוגים, Requests, Diagnostics, בדיקת GPU ומצב מנוע.
- backend להורדת מודל, הצגת התקדמות ובדיקת SHA-256.
- קטלוג מודלים מוטמע ומטמון מקומי.
- בסיס לבדיקת עדכון אפליקציה דרך GitHub.
- שלוש חבילות NInferEZ Engine ציבוריות מאותו קו גרסה: `sm86`, ‏`sm89` ו־`sm120a`, עם
  ZIP, ‏SHA-256 ו־manifest ב־GitHub Release אחד ו־`channel-manifest.json` ציבורי.
- ה־build של `sm120a` מתחיל בפועל על RTX 5080 ומזהה אותו כ־Blackwell `sm120` עם 84 SMs;
  הכרטיס עדיין מסומן Community Preview עד השלמת בדיקת inference מלאה.
- חבילות Installer, Portable ו־Test Portable.

### 3.2 מגבלות המימוש הנוכחי

- הקטלוג המקוון הנוכחי נשען על קריאת model cards ממאגר צד שלישי; הוא אינו feed מוצרי שבשליטת NInferEZ.
- עדכון מקוון אינו יכול לעדכן באופן מלא פרופילי הרצה והמלצות חומרה.
- אין הפרדה מלאה בין מודלים מותקנים לבין מודלים מומלצים להורדה.
- אין מנגנון מסודר לזיהוי מודלים חדשים ולסימון `NEW`.
- המנוע עדיין נארז באפליקציה כעותק יחיד ואין כיום ספריית מנועים, החלפה, rollback או
  עדכון עצמאי דרך דף `Engine`.
- אין resolver מרכזי שמחשב פרופיל בטוח לפי VRAM, מנוע ומודל.
- לקוח ה־UI משתמש כרגע ב־HttpClient עם timeout כללי של 15 שניות גם לפעולת `engine/load`.
  ה־backend עצמו מוכן להמתין עד 120 שניות, אך ביטול בקשת ה־UI מועבר אליו ויכול לעצור את
  ההפעלה בזמן כיול ראשון.
- שירות עדכון האפליקציה כולל בסיס להורדה, אימות והחלפת Portable, אך כתובת GitHub Release
  עדיין אינה מחוברת ותהליך Installed upgrade עדיין אינו שלם.
- גרסת הקוד והחבילות הנוכחית היא `0.1.0`; היא תוחלף בגרסת המוצר המבוקשת `0.0.1` בתחילת המימוש.

### 3.3 ממצא RTX 5080 מהלוג

`manager.log` מ־2026-09-28 מציג שלוש התחלות נפרדות של אותו מנוע:

- בכל ניסיון המנוע מדווח `starting engine` ואז
  `calibrating routes for nvidia-geforce-rtx-5080-sm120 (84 SMs)`.
- ההתחלות מופיעות בהפרשים של כ־12–17 שניות, ללא הודעת CUDA, ‏OOM, קריסה או exit code.
- התבנית תואמת ל־timeout של 15 שניות ב־`BackendClient`: בקשת ה־UI מתבטלת בזמן הכיול,
  ה־cancellation מגיע ל־`EngineController`, התהליך נהרג, והניסיון הבא מתחיל כיול מחדש.
- עלייה של בערך 1GB בזיכרון לבדה אינה מעידה על כשל; היא יכולה להיות שלב ראשוני בהקצאת
  runtime/כיול. בלי שורת exit או OOM אין בסיס לייחס את האירוע למחסור בזיכרון.

לכן תיקון של 15 ל־60 שניות בלבד אינו מספיק. טעינה וכיול יהפכו למבצע backend אסינכרוני
שאינו תלוי במשך בקשת HTTP אחת, עם ביטול מפורש ו־watchdog לפי שלב וחיות התהליך.

## 4. מדיניות גרסאות ואחסון

### 4.1 גרסת מוצר לעומת בניית פיתוח

- גרסת המוצר היחידה בקוד, ב־assembly, ב־API, ב־Installer ובממשק תהיה `0.0.1`.
- איטרציות מקומיות לא ישנו את גרסת המוצר. הן יקבלו `buildId` נפרד: `dev-001`, `dev-002`, וכן הלאה.
- מועמדות להפצה יקבלו `rc-001`, `rc-002`.
- הבנייה שתאושר תהפוך ל־`final` ללא שינוי קוד וללא החלפת גרסת המוצר.
- `buildId` הוא metadata לבדיקות ולא גרסה שמוצגת כגרסת המוצר.

### 4.2 מבנה מוצע במחשב

```text
NInferEZ Manager/
  artifacts/
    0.0.1/
      dev-001/
        App/
        build-info.json
        SHA256SUMS.txt
        test-results/
      dev-002/
      rc-001/
        Installer/
        Portable/
        Test-Portable/
        build-info.json
        SHA256SUMS.txt
      final/
        Installer/
        Portable/
        Test-Portable/
        build-info.json
        SHA256SUMS.txt
  cache/
    engines/
      <engine-package-id>/
        <engine-version>/
    downloads/
  docs/
  src/
  tests/
```

כל `build-info.json` יכלול לפחות:

- `productVersion`: תמיד `0.0.1` בסדרה זו.
- `buildId`.
- Git commit.
- תאריך בנייה.
- channel: `development`, `release-candidate` או `stable`.
- מזהי מנוע וקטלוג ששימשו בבדיקה.
- hashes של כל artifact.
- תוצאות בדיקות וקישור לדוח.

### 4.3 מניעת בזבוז אחסון

- בניות `dev` ישתמשו במטמון מנועים משותף ולא ישכפלו מנוע של יותר מ־1GB בכל תיקייה.
- חבילות מלאות שכוללות מנוע ייווצרו רק ל־RC, ל־final או כאשר נדרשת בדיקה מפורשת של האריזה.
- קובצי מודל לעולם לא ישוכפלו לתיקיות build; האפליקציה תשתמש בתיקיית `Models` או בקישור ששמר המשתמש.
- מחיקת גרסאות ישנות תהיה פעולה ידנית ומפורשת בלבד.
- הבנייה הקיימת ב־`dist/0.1.0` תישמר כ־baseline עד ש־`0.0.1` תאושר; לא מוחקים או מעבירים אותה בתחילת העבודה.

## 5. ארכיטקטורת עדכונים מרחוק

יש להפריד בין שלושה ערוצי מידע:

| Feed | תפקיד | קצב בדיקה |
|---|---|---|
| Model Catalog | מודלים מומלצים, קישורים, hashes ופרופילים | ברקע אחרי עליית UI, פעם ב־24 שעות, או ידנית |
| Engine Channel | מנועים זמינים, תאימות, גרסאות ו־release notes | ברקע אחרי עליית UI, פעם ב־24 שעות, או ידנית |
| App Releases | עדכוני NInferEZ Manager עצמו מ־GitHub Releases | אחרי עליית UI לפי ההעדפה, או בכפתור `Check for updates` |

### 5.1 עקרונות feed

- כל feed יהיה JSON עם `schemaVersion`, מספר revision ותאריך פרסום.
- כתובת ה־feed תוגדר במקום מרכזי אחד ולא תפוזר בקוד.
- ה־feed ייטען מ־GitHub דרך HTTPS ויישמר במטמון מקומי.
- ייעשה שימוש ב־ETag/`If-None-Match`, כך שבדיקה ללא שינוי תהיה קטנה ומהירה.
- האפליקציה לא תבצע polling רציף. בדיקה אחת לאחר שהממשק כבר עלה לא תחסום את ההפעלה.
- אם אין רשת או שה־feed אינו תקין, האפליקציה תשתמש בעותק האחרון התקין. אם אין מטמון, היא תשתמש snapshot מוטמע.
- עדכון מטמון יהיה אטומי: מורידים לקובץ זמני, מאמתים, ואז מחליפים.
- JSON לא תקין, schema לא נתמך או ערך מסוכן יידחו בלי לפגוע במצב הפעיל.

### 5.2 אבטחה

- כל artifact יוגדר עם URL, גודל ו־SHA-256.
- מודל מ־Hugging Face יוצמד ל־revision/commit קבוע ולא ל־`main` משתנה.
- חבילת מנוע תותקן רק לאחר אימות hash ותוכן manifest פנימי.
- חילוץ ZIP יחסום path traversal וקבצים מחוץ לתיקיית staging.
- קטלוג מודלים יהיה data-only; הוא לא יוכל להעביר פקודות, environment variables או executable חלופי.
- לפני הפצה ציבורית יציבה, manifests של מנועים יקבלו חתימה דיגיטלית שנבדקת מול public key שמוטמע באפליקציה. SHA-256 לבדו נשאר חובה גם כאשר קיימת חתימה.

### 5.3 בעלות על הקטלוג

מומלץ לשמור את Model Catalog במאגר data-only נפרד, כדי שניתן יהיה להוסיף מודלים ופרופילים בלי להוציא גרסת אפליקציה. הכתובת הסופית תיקבע בזמן המימוש לאחר בחירת המאגר; אין לקבע URL מומצא בקוד.

Engine Channel יתבסס בתחילה על `channel-manifest.json` של מאגר NInferEZ Engine הקיים.

### 5.4 מקור עדכון האפליקציה

- האפליקציה תבדוק רק GitHub Releases של המאגר הציבורי הרשמי של NInferEZ Manager; היא לא
  תוריד binaries מ־branch, מ־Actions artifact זמני או מ־URL שמגיע מהגדרות משתמש.
- כתובת ה־API, בעל המאגר ושם המאגר יוגדרו ב־`ProductInfo` יחיד ויכוסו בבדיקה; השדה
  הריק הקיים ב־`UpdateService.ReleasesApi` יבוטל.
- Stable יקרא את ה־Release האחרון שאינו draft/prerelease. משתמש שבחר Preview יקבל גם
  prerelease. אין השוואה טקסטואלית בלבד: גרסה ו־channel ינותחו ויאומתו.
- הנכס ייבחר לפי מצב ההפצה: Setup ל־Installed ו־Portable ZIP ל־Portable. שם הקובץ,
  גודל, SHA-256, חתימה כאשר תהיה ו־minimum OS ייבדקו לפני הצעת התקנה.
- בדיקה אוטומטית תתבצע רק לאחר שהחלון וה־API המקומי מוכנים, לכל היותר פעם ב־24 שעות
  כברירת מחדל. הכפתור `Check for updates` תמיד מבצע בדיקה חדשה ומציג תוצאה גם כשאין עדכון.
- כשל GitHub, ‏rate limit או מצב offline אינם חוסמים את האפליקציה ואינם מוחקים מידע מקומי.

## 6. דף Models החדש

### 6.1 החצי העליון — הספרייה המקומית

החצי העליון יישאר באותה שפת עיצוב והתנהגות שאושרו:

- מודלים שנמצאים בתיקיית `Models`.
- מודלים חיצוניים שהמשתמש קישר.
- רענון סריקה.
- קישור מודל חיצוני.
- חיפוש ובחירה.
- עריכת שם תצוגה, פרופיל והגדרות המודל.
- הצגת סטטוס: זמין, חסר, פגום, טעון או פעיל.

מודל שהוסר מהמיקום המקושר לא ייעלם בשקט: הוא יוצג כ־Missing עם אפשרות לאתר מחדש או להסיר את הקישור.

### 6.2 החצי התחתון — Recommended models

יוצג אזור נפרד וברור, ולא ערבוב עם הספרייה המקומית:

- כותרת `Recommended models`.
- זמן רענון אחרון וכפתור Refresh.
- badge עם מספר פריטים חדשים.
- כרטיס לכל מודל עם:
  - שם קצר וברור.
  - תיאור תמציתי.
  - גודל הורדה.
  - פורמט/quantization.
  - תמיכת vision אם קיימת.
  - VRAM משוער ו־context מומלץ למחשב הנוכחי.
  - סטטוס התאמה למנוע הפעיל.
  - קישור לעמוד Hugging Face.
  - פעולה יחידה ודינמית: `Download`, התקדמות, `Resume`, `Installed` או `Use` בהתאם למצב.

ברירת המחדל לא תציג קטלוג גדול ורועש. ב־0.0.1 יוצגו שני המודלים שלנו בלבד. ניתן יהיה להוסיף מודלים ל־feed בהמשך ללא עדכון אפליקציה.

### 6.3 שני המודלים הראשונים

#### Swift 1.5 Qwen3.8-27B Uncensored NVFP4

- Hugging Face: `2beng2/Swift-1.5-Qwen3.8-27B-Uncensored-NVFP4-NInfer`
- קובץ: `Swift_1_5_qwen3_8_27b_uncensored_nvfp4.ninfer`
- גודל ידוע: 22,783,241,220 bytes.
- SHA-256 ידוע: `3729a8a74358e0c1e9a729778fa3d44edad8375005594c0bc91a3dc95812354b`.
- Vision: כן.
- בסיס פרופיל קיים: NVFP4 KV, DFlash2 K7, `lm-head-draft`, prefill 2048.
- context בסיסי: 262,144; מקסימום מוגדר: 524,288, בכפוף ל־VRAM ולבדיקה.

#### OrcaRouter Qwen3.8-27B Uncensored IQ3_XXS

- Hugging Face: `2beng2/Qwen3.8-27B-OrcaRouter-GSQ-RCO-IQ3_XXS-NInfer`
- קובץ: `orca-router-iq3-xxs.ninfer`.
- גודל ידוע: 10,796,696,832 bytes.
- SHA-256 ידוע: `947d2c5197d72518eb1a749e7d24b22582249880f6437c76b7c1289ef8a72b08`.
- Vision: לא.
- בסיס פרופיל קיים: RK8V4, MTP3, `lm-head-draft`, prefill 4096, GDN FP16, MTP attention window 8192.
- context בסיסי: 262,144; מקסימום מוגדר: 524,288, בכפוף ל־VRAM ולבדיקה.

הנתונים יישארו גם ב־snapshot המקומי כדי שהמסך יעבוד offline. ה־feed המרוחק יוכל לעדכן תיאור, release notes ופרופילים, אך לא יחליף קובץ או hash תחת אותו artifact ID.

### 6.4 הורדה אמינה

- בדיקת מקום פנוי לפני התחלה.
- הורדה לקובץ `.part`.
- תמיכה ב־HTTP Range והמשך לאחר סגירה כאשר השרת תומך בכך.
- התקדמות, מהירות ו־ETA בתוך הכרטיס.
- Cancel ו־Resume בלי חלון נוסף.
- תור הורדות; ברירת המחדל תהיה הורדה פעילה אחת כדי לא להעמיס על דיסק ורשת.
- אימות גודל ו־SHA-256 לפני הצגת `Installed`.
- rename אטומי לשם הסופי.
- שגיאה קריאה למשתמש ולוג טכני מלא.
- לאחר הורדה: יצירת פרופיל מומלץ והוספת המודל לספרייה, ללא טעינה אוטומטית ל־VRAM.

### 6.5 מודלים חדשים והתראות

- האפליקציה תשמור מקומית אילו model IDs המשתמש כבר ראה.
- `NEW` יוצג רק לפריט חדש ביחס למצב המקומי, ולא לפי דגל קבוע בשרת.
- badge במסך Models יציג את מספר המודלים החדשים.
- כניסה לכרטיס או בחירה ב־`Mark as seen` תסיר את הסימון.
- עדכון פרופיל למודל קיים יוצג כ־`Profile update available`, לא כמודל חדש.
- התראות יהיו בתוך האפליקציה; tray notification יהיה אופציונלי ולא ברירת מחדל.

## 7. בחירת פרופיל לפי חומרה ומודל

### 7.1 מצב ברירת מחדל

כל מודל יקבל `Auto — fastest safe`:

1. זיהוי GPU, compute capability, VRAM וגרסת driver.
2. בחירת המנוע המומלץ שמתאים טכנית לארכיטקטורה.
3. בחירת שיטת ההרצה המהירה ביותר שנבדקה עבור artifact זה.
4. חישוב context מרבי בטוח לפי תקציב VRAM.
5. שמירת margin למערכת, workspace ולתשובה.
6. שמירת הפרופיל והסבר קצר: מה נבחר ולמה.

המילה “מהיר” מתייחסת לפרופיל מאומת לאותו מודל. האפליקציה לא תחליף את המודל בכימות איכות נמוך יותר רק כדי להגדיל מהירות.

### 7.2 חישוב context

ה־resolver ישתמש בנתונים מהקטלוג:

- VRAM בסיסי של משקלי המודל.
- עלות KV לכל 1K tokens לכל שיטת KV.
- workspace של המנוע.
- עלות speculative/vision אם קיימת.
- buffer בטיחות.
- context מקסימלי שהמודל והפרופיל מתירים.

ה־context שיוצע יהיה הערך הגדול ביותר שעומד בכל התנאים. אין להשתמש רק בשם הכרטיס או בטבלת if/else קשיחה.

### 7.3 Auto מול Custom

- `Auto` מתעדכן כאשר חומרה, מנוע או קטלוג משתנים.
- `Custom` לעולם לא יוחלף בשקט.
- אם קיים פרופיל חדש, משתמש Custom יקבל הצעה ולא שינוי אוטומטי.
- לפני הפעלה יוצג warning אם ההערכה מצביעה על OOM, עם פעולה אחת להחזרת ההמלצה הבטוחה.

## 8. דפי Engine ו־Settings

לא יתווספו טאבים ל־Settings. ניהול מנוע וחומרה שייך לדף `Engine` הקיים; Settings נשאר
מסך ההעדפות והפרופיל שאושר, בלי לערבב התקנת runtime עם הגדרות שימוש.

### 8.1 דף Engine — תמונת מצב

החלק העליון ישמור את שפת העיצוב הקיימת ויציג:

- המנוע הפעיל, גרסה, channel, ‏build ID ו־contract version.
- GPU מזוהה, VRAM, compute capability וה־engine target שנבחר.
- סטטוס אחד ברור: Ready, Loading, Update available, Restart required, Missing,
  Incompatible או Error.
- יכולות שמגיעות בפועל מ־manifest/capabilities. לא יוצג NVFP4 בכרטיס או build שאינם תומכים בו.
- פעולה ראשית דינמית אחת: Load, Cancel loading, Unload, Retry או Install engine.

### 8.2 דף Engine — מנוע מומלץ וספריית מנועים

מתחת לתמונת המצב יהיו שני אזורים רספונסיביים באותו דף:

**Recommended for this GPU**

- המלצה לפי compute capability ולא לפי התאמת מחרוזת לשם הכרטיס.
- שם ידידותי, למשל `RTX 5000 Series`, ולצדו פרטים טכניים מתקפלים:
  `sm120a`, Windows x64, גרסה, channel וגודל.
- qualification אמיתי: `Tested` רק לדגם שנבדק; `Compatible — community preview` לכרטיס
  מאותה ארכיטקטורה, כגון RTX 5080, שעדיין לא עבר qualification מלא.
- פעולה אחת שמשתנה בין Download, Install, Activate, Update או Active.

**Installed and available engines**

- גרסאות מותקנות וגרסאות זמינות מ־`channel-manifest.json` של NInferEZ Engine.
- `Refresh engines` לרענון ה־feed ו־`Check for updates` לעדכון המנועים בלבד.
- release notes, ‏SHA-256, source URL, qualification והתאמת GPU לכל חבילה.
- Activate/Roll back כפעולה דינמית. Remove יופיע רק למנוע לא פעיל ולא למנוע האחרון התקין.
- בחירה ידנית של build לא מומלץ תישאר תחת Advanced ותציג את הסיבה והסיכון לפני ביצוע.

### 8.3 כללי התקנה ומעבר בין מנועים

- מנועים נשמרים בתיקיות immutable לפי package ID, גרסה וארכיטקטורה.
- הורדה נכתבת ל־`.part`, תומכת resume, עוברת גודל, SHA-256, manifest ו־safe extraction.
- אין להחליף קבצים של מנוע פעיל במקום.
- מעבר יתבצע רק כאשר אין בקשות פעילות והמנוע אינו טעון. אם הוא פעיל, המשתמש יקבל פעולה
  אחת של `Unload and switch`; אין הרג באמצע בקשה.
- לאחר activation תתבצע בדיקת identity/capabilities ו־host-only probe. הפעלה ראשונה מוצלחת
  מסמנת את הגרסה כ־last known good.
- במקרה כשל ה־pointer חוזר אוטומטית למנוע האחרון התקין. הגרסה הקודמת נשמרת עד שהחדשה
  עברה לפחות startup ו־health מוצלחים.
- ניקוי מנועים ישנים מתבצע רק אחרי הצלחה, אינו נוגע במנוע פעיל ואינו מוחק את ה־rollback
  היחיד ללא בחירה מפורשת.

### 8.4 Settings ללא כפתור Save changes כללי

- הכפתור העליון `Save changes` יוסר.
- פרופיל מודל ישמור דרך הפעולה המקומית הקיימת `Save profile`; ‏`Restore tested defaults`
  ימשיך להיות מפורש ולא יתבצע אוטומטית.
- שינויי API שדורשים restart ישמרו ויופעלו רק דרך `Apply API changes`, כדי שלא להחליף פורט
  באמצע עריכת שדה.
- Theme, ‏auto-unload, זמן idle, ‏Start with Windows, ‏Start minimized, ‏Close to tray,
  בדיקות קטלוג ועדכונים ושאר העדפות בטוחות יישמרו אוטומטית.
- Text/Number fields יישמרו ב־LostFocus/Enter או לאחר debounce של 500–800ms; toggles ו־ComboBox
  יישמרו מיד. כתיבה תהיה אטומית ולא בכל keypress.
- ליד שדה שנשמר יוצג מצב קטן `Saved`; בכשל יוצג inline error והערך האחרון התקין לא יידרס.
- ניווט מהדף או סגירת החלון יבצע flush לשינוי תקין שממתין ל־debounce. ערך לא תקין יישאר
  מסומן ולא יישמר בשקט.
- מבנה Settings הקיים נשאר רספונסיבי ומחולק ל־Inference profile, ‏Local API,
  Memory and startup, ‏Maintenance ו־Advanced. לא יתווסף TabControl.

### 8.5 מצב ללא מנוע

- הממשק וה־API הציבורי 8173 יעלו כרגיל ללא הקצאת GPU.
- `/health` יציין שה־Manager פעיל אך אין inference engine.
- בקשת inference תחזיר שגיאה מבנית `engine_not_installed` עם פעולה לפתיחת דף Engine.
- דף Engine יציג setup מלא inline; אין wizard או חלון Setup נפרד שנדרש להפעלה רגילה.

## 9. זיהוי חומרה ותאימות מנוע

ה־manifest יפריד בין:

- `displayFamily`: למשל `RTX 5000 Series`.
- `cudaArchitecture`: למשל `sm120a`.
- `testedGpuModels`: רשימת כרטיסים שנבדקו בפועל.
- `supportedPlatform`: Windows x64.
- `minimumDriver` ו־`minimumManagerContract`.
- qualification/channel.

כך ניתן להמליץ על אותה חבילת `sm120a` לכרטיס תואם מסדרת 5000 בלי לטעון שכל הסדרה נבדקה. מנוע בעל ארכיטקטורה לא תואמת ייחסם; מנוע בעל אותה ארכיטקטורה אך ללא qualification מלא יוצג כ־community preview ויהיה ניתן לבחור בו ידנית.

מטריצת הבחירה לגרסה זו:

| זיהוי חומרה | חבילת מנוע | הצגה למשתמש | NVFP4 weights |
|---|---|---|---|
| compute capability 8.6 | `sm86` | RTX 3000 Series compatible; פרופיל ידוע ל־3090/3090 Ti | לא |
| compute capability 8.9 | `sm89` | RTX 4000 Series compatible; פרופיל ידוע ל־4090 | לא |
| Blackwell compute capability 12.0, כולל דיווח runtime כ־`sm120` | `sm120a` | RTX 5000 Series compatible; פרופילים ידועים ל־5090/RTX PRO 6000 | כן |

כללי 5080:

- `RTX 5080` שמדווח `sm120` לא ייחסם בגלל שהחבילה נקראת `sm120a`; resolver ממפה את
  משפחת Blackwell 12.0 ל־package target `sm120a` לאחר אימות manifest ודרייבר.
- הוא יקבל `Compatible — community preview`, לא `Tested`, עד שבדיקת טעינה ו־inference מלאה
  תועדה על 5080.
- פרופיל 5090 לא יועתק אליו עיוור. Auto context יחושב מ־VRAM בפועל, עלויות המודל ו־margin,
  וכיול המסלולים של המנוע יורשה להסתיים בפעם הראשונה.
- מודל NVFP4 יוצע רק עם build `sm120a`; ב־sm86/sm89 יוצעו מודלי GSQ-RCO/פורמטים תואמים בלבד.

### 9.1 טעינה, כיול ו־timeout לכל הכרטיסים הנתמכים

פעולת טעינה לא תישאר בקשת HTTP פתוחה עד READY:

1. `POST /control/v1/engine/load` מאמת preflight, יוצר operation יחיד ומחזיר מיד `202`
   עם `operationId`.
2. ה־backend הוא הבעלים של process ושל `CancellationTokenSource`; ניתוק UI או timeout של
   HttpClient אינם מבטלים את המנוע.
3. `GET /control/v1/engine/operation` מחזיר state, phase, elapsed time, PID, model, engine,
   startup log tail ויכולת cancel.
4. `POST /control/v1/engine/operation/cancel` מבטל את הפעולה, ממתין לסגירה מסודרת ואז מסיים
   רק את process tree שה־Manager פתח.
5. קריאת load נוספת בזמן פעולה קיימת אינה מפעילה process שני; היא מחזירה את אותו operation
   או `load_in_progress` מובנה.

שלבי ה־progress יהיו לפחות: `Starting engine`, ‏`Calibrating GPU routes`, ‏`Loading model`,
`Waiting for API` ו־`Ready`. הזיהוי יגיע משורות מנוע מובנות ככל שניתן; אם אין אחוז אמיתי,
הממשק יציג טבעת indeterminate וזמן שחלף ולא אחוז מומצא.

מדיניות הזמן:

- פעולות control קצרות נשארות עם timeout של 15 שניות.
- פעולת load/restart אינה משתמשת ב־timeout הכללי של `BackendClient`.
- טעינה חמה בכרטיס שכבר כויל מקבלת hard ceiling התחלתי של 5 דקות.
- כיול ראשון או GPU לא מוכר מאותה ארכיטקטורה, כולל 5080, מקבל hard ceiling של 15 דקות.
- ה־backend בוחר את המסלול לפי קיום device profile/cache ולא רק לפי שם 3090/4090/5090.
- health polling משתמש בבקשות קצרות של שנייה, אך כשל probe יחיד אינו כשל startup.
- אם process יוצא, מוצגים מיד exit code ושלוש שורות הלוג האחרונות. אם הוא חי ומדווח התקדמות,
  אין timeout של 60 שניות שמפסיק אותו.
- לאחר 60 שניות יוצג רק מסר `Still working` עם השלב והזמן, לא שגיאה. המשתמש יכול לבטל בכל רגע.

המספרים הם גבולות בטיחות, לא הבטחת זמן. יש למדוד cold/warm startup בפועל על 3090, ‏4090,
5080 ו־5090 ולכוון אותם לפני Stable; עד אז הבחירה השמרנית מונעת קטיעה של כיול תקין.

## 10. אחסון runtime

### 10.1 Portable

```text
NInferEZ Manager/
  App/
  Backend/
  Data/
    CatalogCache/
    Downloads/
    Logs/
    Settings/
  Engines/
    <package-id>/<version>/
  Models/
  Docs/
  NInferEZ Manager.exe
```

כל הנתונים נשארים ליד ה־Portable. אין כתיבה לנתיבי התקנה אחרים מלבד cache זמני של Windows בעת הצורך.

### 10.2 Installed

- קבצי האפליקציה נמצאים בתיקיית ההתקנה.
- נתונים משתנים, engines, logs, cache ו־downloads נשמרים תחת `%LOCALAPPDATA%\NInferEZ Manager`.
- Models שנבחרו בזמן התקנה יישמרו בנתיב המשתמש ולא ב־Program Files.
- uninstall יציע במפורש אם להשאיר Models ו־Data; הוא לא ימחק מודלים אוטומטית.

### 10.3 חבילות ההפצה הראשונות

כדי לשמור על ההתנהגות הקיימת ועל מוצר מוכן לשימוש:

- ה־Installer וה־Test Portable הראשונים של `0.0.1` יוכלו להגיע עם מנוע RTX 5000 Series שכבר אושר, ובמקביל לרשום אותו בספריית המנועים החדשה.
- Portable נקי יוכל להציע הורדה בתוך האפליקציה אם המנוע אינו כלול.
- לאחר שמנגנון ההורדה וה־rollback יוכח, ניתן יהיה לעבור לחבילות thin שאינן משכפלות מנוע. זו החלטת packaging בלבד ולא שינוי בארכיטקטורת האפליקציה.

## 11. State, contracts ומיגרציה

יש להוסיף ל־settings schema:

- `schemaVersion`.
- `activeEnginePackageId` ו־`activeEngineVersion`.
- `catalogChannel`, ‏`engineChannel` ו־`appChannel`.
- timestamps ו־ETags של refresh אחרון.
- מודלים ומנועים שהמשתמש כבר ראה.
- auto-check toggles.
- מצב profile: Auto או Custom.
- source revision של כל פרופיל מומלץ.
- last known good engine והיסטוריית activation מוגבלת.
- מצב post-update/migration אחרון, בלי לשמור token או credential של GitHub.

יש להוסיף חוזה `EngineOperation` משותף ל־UI ול־backend:

- `operationId`, ‏`kind`, ‏`state`, ‏`phase` ו־`startedAt`.
- `elapsedSeconds`, ‏`canCancel`, ‏`modelId`, ‏`enginePackageId` ו־`processId` כאשר קיים.
- `lastProgressAt`, ‏`startupMessage`, ‏`errorCode`, ‏`errorMessage` ו־`exitCode`.
- states סופיים: `ready`, ‏`cancelled`, ‏`failed` או `timed_out`; אין חזרה שקטה ל־Unloaded
  שמסתירה את סיבת הסיום.
- operation state נשמר מספיק זמן כדי ש־UI שנפתח מחדש יציג את התוצאה, אך אינו משחזר
  process שכבר אינו חי.

מיגרציה חייבת לשמר:

- מודלים מקושרים.
- aliases שמשמשים ב־API.
- profiles קיימים.
- פורט 8173 והגדרות API.
- unload timeout.
- נתיבי Models, Logs ו־Data.

הגדרה לא מוכרת לא תקריס את האפליקציה. מיגרציה תיצור backup לפני כתיבה ותהיה ניתנת ל־rollback.

## 12. התנהגות עדכונים

### 12.1 מודל

- artifact חדש עם ID חדש יוצג כמודל חדש.
- שינוי description אינו דורש הורדה מחדש.
- profile חדש יוצע בנפרד.
- שינוי weights מחייב artifact ID, revision ו־hash חדשים; אין החלפה שקטה.

### 12.2 מנוע

- האפליקציה תוריד גרסה חדשה לצד הקיימת.
- אימות מלא יתבצע לפני activation.
- activation לא תקרה באמצע inference.
- update אוטומטי לא יהיה ברירת מחדל ב־0.0.1; תהיה בדיקה אוטומטית והתקנה באישור המשתמש.
- rollback יהיה זמין למנוע הקודם.

### 12.3 אפליקציה

- עדכון אפליקציה נשאר ערוץ נפרד ממודלים ומנועים. עדכון אחד אינו גורר את האחרים כאשר
  schema ו־contract נתמכים.
- לאחר עליית ה־UI תתבצע בדיקה שקטה לפי `AutoCheckUpdates` ו־`UpdateCheckHours`. עדכון זמין
  יוצג כ־InfoBar/badge לא פולשני עם גרסה, גודל, release notes ופעולת `Download update`.
- בדף Settings, בכרטיס Maintenance, יהיה כפתור `Check for updates`. בזמן בדיקה הוא מציג
  spinner, ולאחריה `Up to date`, ‏`Update available` או שגיאה עם Retry. אותו command יהיה זמין ב־tray.
- הורדת עדכון רצה ברקע עם progress, ‏ETA ו־Cancel; היא אינה חוסמת ניווט, API או inference.
- קובץ נשמר ב־Updates staging, נבדק לפי גודל, SHA-256, שם asset, channel וחתימה כאשר זמינה.
  רק package שעבר אימות יקבל `Install and restart`.

**Installed**

- מופעל Setup החדש במצב upgrade מתוך helper נפרד, כדי שקובצי האפליקציה הישנים לא יהיו נעולים.
- ה־Manager וה־backend נסגרים מסודר; inference פעיל דורש אישור לעצירה או דחיית העדכון.
- ה־Installer מחליף את קובצי האפליקציה בלבד, מסיר קבצים ששייכים לגרסה הישנה ושומר
  Models, ‏Data, ‏Logs, ‏aliases, profiles ומנועים.
- לאחר ההתקנה האפליקציה החדשה עולה עם `--post-update`, מבצעת health/schema migration ומסמנת
  הצלחה. uninstall entry וקיצורי הדרך מתעדכנים לאותה התקנה, בלי שתי גרסאות ברשימה.

**Portable**

- updater/helper חיצוני ממתין לסגירת UI וה־backend, מחלץ ל־staging ומאמת את התוכן.
- הוא מחליף אטומית `App`, ‏`Backend`, ‏`Docs` וקובצי root השייכים למוצר, אך שומר
  `Data`, ‏`Models`, ‏`Engines`, ‏`portable.mode` וקבצים שאינם בבעלות האפליקציה.
- הגרסה הישנה מועברת זמנית ל־backup. לאחר שהגרסה החדשה עלתה, עברה health ומיגרציה, ה־backup
  וה־ZIP נמחקים. אם העלייה נכשלת, ה־helper מחזיר את הגרסה הישנה.
- בסיום נשארת אצל המשתמש גרסת אפליקציה פעילה אחת; לא מצטברות תיקיות version ישנות.

- אם feed דורש schema חדש, יוצג `Manager update required` ולא יתבצע ניחוש.
- אין forced update ב־0.0.1. המשתמש מאשר התקנה, יכול לבחור Later, ועדכון שהורד לא יותקן
  בזמן בקשה פעילה.

## 13. ביצועים וצריכת משאבים

- הממשק יעלה לפני בדיקות רשת.
- feed refresh יתבצע פעם אחת ברקע ורק כאשר הגיע הזמן או כשהמשתמש לחץ Refresh.
- conditional requests ימנעו הורדות JSON מיותרות.
- GPU detection יישמר במטמון וירוץ מחדש רק כאשר נדרש.
- חישוב פרופיל הוא חישוב metadata ואינו טוען מודל או מנוע ל־VRAM.
- לא יהיה service נוסף שרץ מחוץ לאפליקציה כאשר אין inference.
- downloader ישתמש ב־streaming ולא יטען קבצים גדולים לזיכרון.
- חישוב hash יתבצע כ־stream ובתהליך רקע עם progress.
- סריקת Models תהיה incremental לפי path, size ו־modified time; hash מלא יתבצע רק כאשר צריך אימות.
- מנוע יעלה רק בבקשת inference, בהתאם להתנהגות הקיימת.

## 14. שפת UX

- פעולה אחת לכל מצב במקום זוגות כפתורים מקבילים.
- אפשרויות לא רלוונטיות לא יוצגו.
- פעולות מתקדמות יישארו מאחורי `Advanced`.
- status, progress ושגיאה יוצגו בתוך הרכיב שאליו הם שייכים.
- אין dialog אם ניתן להשלים פעולה באופן ברור בתוך העמוד.
- שגיאה תכלול ניסוח אנושי ו־`View details` ללוג הטכני.
- הורדה או עדכון לא יחסמו ניווט באפליקציה.
- layout יהיה responsive ולא ישאיר שטחים ריקים ללא תפקיד.
- badges יהיו קטנים ולא יהפכו את הניווט למרכז התראות עמוס.
- בזמן טעינת מנוע הפעולה הראשית הופכת באותו מקום ל־`Cancel`; לא מוצגים Load ו־Cancel
  במקביל ולא ניתן להתחיל instance נוסף.
- טבעת ההתקדמות תהיה indeterminate כל עוד למנוע אין אחוז אמיתי. במרכזה יוצג זמן שחלף
  (`00:42`) ומתחתיה שלב אנושי כמו `Calibrating RTX 5080` או `Loading model weights`.
- לאחר דקה הטקסט יסביר שהכיול הראשון בכרטיס חדש עשוי לקחת מספר דקות. זו אינדיקציה בלבד,
  לא timeout ולא popup שגיאה.
- ביטול מציג `Cancelling…`, מושבת לאחר הלחיצה ומסתיים רק לאחר שחרור ה־process והמשאבים.
- אם הטעינה נמשכת והמשתמש מנווט לעמוד אחר, סטטוס קומפקטי נשאר בכותרת/Engine badge; חזרה
  לדף Engine מציגה את אותו operation והזמן ממשיך, בלי להתחיל מחדש.

## 15. בדיקות נדרשות

### 15.1 Unit tests ללא GPU

- parsing ו־validation לכל schema.
- manifest תקין, פגום, ישן וחדש מדי.
- cache fallback במצב offline.
- ETag ו־304.
- זיהוי מודל חדש ו־Mark as seen.
- בחירת engine package לפי architecture.
- חסימת engine לא תואם.
- חישוב profile/context עבור כמה רמות VRAM.
- Auto אינו דורס Custom.
- hash/size mismatch.
- ZIP path traversal.
- settings migration ו־rollback.
- מניעת מחיקת מנוע פעיל/אחרון תקין.
- מיפוי compute capability 12.0/`sm120` ל־package `sm120a`, כולל RTX 5080.
- load operation state machine, idempotency, cancel ו־hard ceilings של warm/calibration.
- autosave debounce, flush בניווט, validation ושמירה אטומית ללא `Save changes` כללי.
- בחירת GitHub asset נכונה ל־Installed/Portable ולערוץ Stable/Preview.

### 15.2 Integration tests ללא GPU

- שרת HTTP מדומה להורדה שנקטעת וממשיכה.
- הורדת מודל, אימות, rename והופעה בספרייה.
- הורדת engine package מדומה, staging, validation ו־activation.
- כשל activation וחזרה למנוע הקודם.
- refresh ידני ואוטומטי ללא חסימת UI.
- Portable ו־Installed משתמשים בנתיבי אחסון שונים כמתוכנן.
- API מחזיר `engine_not_installed` כאשר אין מנוע.
- UI client עם timeout קצר לפעולות קצרות אך ללא ניתוק load/restart ארוך.
- engine load מדומה שנמשך מעל 15 שניות ומעל דקה, מדווח progress ומסתיים בלי retry או
  process כפול.
- Cancel בזמן calibration, בזמן model load ואחרי READY race; בכל מקרה נשאר state עקבי.
- update Installed/Portable מדומה: staging, hash, החלפה, post-update health, ניקוי גרסה ישנה
  ו־rollback כאשר הגרסה החדשה אינה עולה.

### 15.3 Hardware tests

- RTX 5090: smoke test מלא עם שני המודלים והמנוע הקיים.
- RTX 5080: השלמת הכיול הראשון, טעינת מודל תואם, בקשת inference, ביטול טעינה, cold/warm
  timings ובדיקת VRAM. עד השלמת שורה זו הסטטוס נשאר Community Preview.
- בדיקת lazy load, unload, context מומלץ ו־API 8173.
- RTX 3090/4090: החבילות הציבוריות `sm86`/`sm89` יוצגו דרך feed כ־Community Preview עד
  qualification קהילתי. האפליקציה תבדוק בחירה, הורדה והפעלה; Stable דורש חומרה אמיתית.
- כרטיסים נוספים באותה compute capability יסומנו Community Preview עד דיווח אמיתי.

### 15.4 Packaging tests

- Installer נקי.
- Portable נקי.
- Test Portable מוכן לבדיקה.
- single instance בכל חבילה.
- icon בכל shell surface.
- upgrade שומר Models, aliases, settings ו־logs.
- upgrade מסיר את קובצי גרסת האפליקציה הישנה לאחר post-update health, בלי למחוק Data,
  Models, Engines או rollback זמני לפני הצלחה.
- uninstall אינו מוחק Models בלי בחירה מפורשת.
- אין נתיב, username, token או מידע אישי בתוך artifacts.
- hashes ודוח build מלאים.

## 16. סדר פיתוח

### שלב 0 — הקפאת baseline

- לתעד את ה־build המאושר הנוכחי ואת hashes שלו.
- לשמור את `dist/0.1.0` ללא שינוי.
- ליצור branch/commit נקודת התחלה.
- לשמור את לוג ה־5080 כ־regression fixture מתומצת ללא נתיב אישי.

### שלב 1 — lifecycle אמין לטעינה ארוכה

- לפצל את timeout של פעולות control קצרות מפעולות load/restart.
- להפוך load ל־operation אסינכרוני עם status, phase, elapsed ו־cancel.
- למנוע retries/processes כפולים ולשמר operation בניווט וב־tray.
- להוסיף מיפוי Blackwell `sm120` ל־package `sm120a` ובדיקת 5080.
- לבנות את טבעת ההתקדמות, הטיימר וכפתור הביטול בתוך שפת העיצוב הקיימת.

### שלב 2 — גרסאות ואחסון

- להעביר את ProductVersion ל־`0.0.1` בכל מקורות הגרסה.
- להוסיף `buildId` נפרד.
- לעדכן build script למבנה `artifacts/0.0.1/<buildId>`.
- להוסיף `build-info.json` ו־hashes.

### שלב 3 — חוזי feed ולקוחות cache

- להגדיר Model Catalog schema ו־Engine Channel schema.
- להוסיף validation, ETag, cache אטומי ו־offline fallback.
- לשמור snapshot מוטמע של שני המודלים ושל המנוע המאושר.
- לחבר App Releases למאגר GitHub הרשמי ולבחירת asset לפי Installer/Portable.

### שלב 4 — Hardware/Profile Resolver

- לרכז זיהוי חומרה.
- לממש engine compatibility.
- לממש `Auto — fastest safe` וחישוב context.
- להגן על Custom profiles.

### שלב 5 — Model recommendations

- לפצל את Models ל־Local Library ול־Recommended.
- להוסיף cards, HF links, badges ו־refresh.
- להוסיף downloader עם resume, disk check, hash ו־atomic install.
- ליצור פרופיל מומלץ לאחר התקנה.

### שלב 6 — Engine Library בדף Engine

- להוסיף registry מקומי של engines.
- download, staging, validation, activation ו־rollback.
- לשנות את EngineController לקבל active engine path מה־registry.
- לשמר lazy start וביצועים קיימים.
- להציג current/recommended/installed/available וכל פעולות המעבר בדף `Engine` הקיים.

### שלב 7 — Settings autosave והתראות

- להסיר את `Save changes` הכללי ללא הוספת טאבים.
- להשאיר `Save profile` ו־`Apply API changes` כפעולות מקומיות; יתר ההעדפות נשמרות אוטומטית.
- להוסיף `Check for updates` בכרטיס Maintenance ולחבר בדיקה שקטה אחרי עליית UI.
- badges ועדכונים לא פולשניים.
- states מלאים: offline, missing, incompatible, updating, failed ו־ready.

### שלב 8 — App updater, QA ואריזה

- unit/integration tests.
- הורדה, אימות והתקנה של App Release ל־Installed ול־Portable.
- post-update health, ניקוי גרסה ישנה ו־rollback אוטומטי בכשל.
- build `dev-*` מהיר ללא שכפול engines.
- build RC מלא.
- בדיקה על RTX 5090.
- בדיקת כל פעולות UI.
- יצירת Installer, Portable ו־Test Portable.
- ניקוי מידע אישי ויצירת hashes.
- promotion של ה־RC המאושר ל־final.

## 17. קריטריוני קבלה ל־0.0.1

הגרסה מוכנה לאישור רק כאשר:

1. האפליקציה עולה מהר ו־API 8173 פעיל בלי לטעון מודל או engine process.
2. Models מציג ספרייה מקומית בחלק העליון ושני מודלים מומלצים בחלק התחתון.
3. ניתן להוריד כל אחד משני המודלים, לעצור, להמשיך ולאמת אותו.
4. המודל שהורד מופיע בספרייה עם פרופיל `Auto — fastest safe` שמתאים לחומרה.
5. קטלוג חדש מתעדכן בלי עדכון אפליקציה ופועל offline מהמטמון.
6. ניהול המנועים נמצא בדף Engine בלבד; Settings נשאר ללא טאבים וללא `Save changes` כללי.
7. `Save profile` ו־`Apply API changes` עובדים מקומית, וכל יתר ההעדפות נשמרות אוטומטית
   ובאופן אטומי בלי לאבד ערכים בניווט או בסגירה.
8. ניתן להוריד engine package, לאמת, להפעיל, לעבור גרסה ולחזור לגרסה קודמת.
9. מנוע RTX 5000 Series מוצג בשם הידידותי, RTX 5080 ממופה ל־`sm120a` ומסומן Community
   Preview עד qualification אמיתי.
10. טעינת מנוע שנמשכת יותר מ־15 שניות או יותר מדקה אינה נכשלת או מתחילה מחדש; מוצגים שלב,
    זמן שחלף, טבעת התקדמות ו־Cancel שעוצר process יחיד בצורה נקייה.
11. אין שינוי מנוע באמצע בקשה ואין דריסה שקטה של Custom profile.
12. `Check for updates` בודק GitHub, ועדכון זמין ניתן להורדה ולהתקנה. לאחר health מוצלח
    קבצי האפליקציה הישנים נמחקים, בעוד Models/Data/Engines/settings נשמרים.
13. כשל בעדכון אפליקציה או מנוע מחזיר אוטומטית לגרסה האחרונה התקינה.
14. Installer, Portable ו־Test Portable עוברים את כל בדיקות האריזה.
15. אין מידע אישי, נתיבים מקומיים או credentials בחבילות.
16. כל build שמור וממוספר, בעוד גרסת המוצר שמוצגת נשארת `0.0.1`.

## 18. החלטות מוצר וטכנולוגיה שנקבעו

ברירת המחדל המומלצת במסמך זה היא:

- Model Catalog כ־feed data-only שבשליטת מאגר NInferEZ Manager, עם snapshot מוטמע לעבודה offline.
- Engine Channel במאגר NInferEZ Engine הקיים.
- App Releases מהמאגר הציבורי הרשמי `BenGamliel/NInferEZ-Manager`.
- ערוץ `stable` כברירת מחדל ו־`preview` תחת Advanced.
- `Auto — fastest safe` כברירת מחדל.
- in-app notifications; tray notifications רק באישור משתמש.
- update אוטומטי לבדיקה בלבד; התקנה באישור משתמש.
- build פיתוח משתמש במנוע משותף; RC/final מקבלים חבילות מלאות.
- שני המודלים שלנו בלבד מוצגים כהמלצות ב־0.0.1.

Portable ו־Installer נקיים אינם משכפלים מנוע. בהפעלה ראשונה ללא מנוע המשתמש חייב לבחור
חבילה מתאימה, כאשר ברירת המחדל מומלצת לפי החומרה. Test Portable רשאי לכלול מנוע לצורך QA בלבד.

## 19. מצב המימוש בפועל

### 19.1 הושלם

- המאגר הציבורי `BenGamliel/NInferEZ-Manager` נוצר, והוגדרו README, רישוי, אבטחה,
  תרומה, ארכיטקטורה ו־GitHub Actions.
- גרסת המוצר אוחדה ל־`0.0.1`; איטרציות מקומיות משתמשות ב־`buildId` נפרד.
- lifecycle טעינת המנוע הוסב לפעולה אסינכרונית משותפת עם status, phase, elapsed,
  ביטול מפורש ו־hard limit של 15 דקות. ביטול בקשת UI אינו הורג עוד את התהליך המשותף.
- מיפוי RTX 30/40/50 Series ל־`sm86`/`sm89`/`sm120a` הוטמע, כולל בדיקה מפורשת ל־RTX 5080.
- ספריית המנועים הוטמעה בדף Engine: refresh, המלצה, הורדה מאומתת, התקנה לצד גרסה קיימת,
  activation, progress וביטול. מנוע bundled תקין מזוהה כמנוע פעיל לצורכי Test Portable.
- Model Catalog מוצרי הוטמע עם שני המודלים, snapshot offline, רענון מרחוק, פרופילים,
  הורדה, hash, progress, pause וקישורי Hugging Face.
- Settings עבר ל־autosave; הפעולות המקומיות `Save profile` ו־`Apply API changes` נשארו מפורשות.
- first-run setup מחייב בחירת מנוע כאשר אין מנוע מותקן, ממליץ לפי GPU ומאפשר בחירה ידנית.
- UpdateService חובר למאגר הרשמי; מנגנוני Portable ו־Installed שומרים Models/Data/Engines
  ומאמתים staging לפני החלפה.
- Installer, Portable נקי ו־Test Portable נבנו מחדש ב־`dev-003` מה־commit `4859fa3`;
  בדיקות פרטיות החבילות, 61 hashes ו־smoke test מתוך החבילה עברו.
- בדיקות backend, build, feed מקוון ו־packaging עברו ללא הפעלת inference על GPU.
- GitHub Actions עבר בהצלחה ב־Windows: restore, build, host-only tests ו־feed validation.
- לוגו `2beng2` נוסף כחתימת `BY` משנית בדף Engine ובתיעוד בלבד; הוא אינו לוגו המוצר.

### 19.2 נותר לפני RC

- לבצע בדיקת ממשק ידנית ל־Portable, כולל first run, Engine, Models, autosave, single instance
  וכל מצבי ההתקדמות והשגיאה שאפשר לבדוק ללא עומס GPU.
- להכין תצוגת ניסיון יחידה לחתימת `BY 2beng2` בתחתית התפריט הפתוח. אין להחיל אותה על
  יתר החבילות לפני אישור חזותי מפורש של המשתמש.
- לבצע בדיקות inference ו־VRAM על RTX 5090 רק לאחר אישור מפורש; RTX 5080/3090/4090 נשארים
  Community Preview עד בדיקה על חומרה אמיתית.
- לאמת תהליך update מקצה לקצה מול Release ניסיוני לפני `final`.
- ליצור `rc-*`, לקבל אישור משתמש, ורק אז לקדם את אותם artifacts ל־`final`/GitHub Release.

### 19.3 מגבלות מכוונות ב־0.0.1

- אין forced update ואין התקנה אוטומטית ללא אישור משתמש.
- אין הבטחת Stable לכרטיס שלא עבר inference על חומרה אמיתית.
- אין חתימה דיגיטלית מלאה ל־manifests עדיין; HTTPS, מקור רשמי, גודל ו־SHA-256 נאכפים.
- חתימת ה־sidebar של `2beng2` היא שינוי UX שממתין לאישור ואינה חלק מה־build המאושר הנוכחי.

