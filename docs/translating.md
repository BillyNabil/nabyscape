# Translating NabyScape

NabyScape's interface is available in English, Español, Bahasa Indonesia, Русский, Українська and 한국어. Corrections and new languages are welcome, and you don't need to be a programmer: every translation is a plain line of text.

## Where the strings live

All interface text is in `src/NabyScape/UI/Shared/`:

| File | Contains |
| --- | --- |
| `LocalizationCatalog.cs` | The list of supported languages (code, native name) |
| `LocalizationCatalog.es.cs` | Spanish |
| `LocalizationCatalog.id.cs` | Indonesian |
| `LocalizationCatalog.ru.cs` | Russian |
| `LocalizationCatalog.uk.cs` | Ukrainian |
| `LocalizationCatalog.ko.cs` | Korean |

Each language file is one list of lines like this:

```csharp
["Check for updates"] = "Проверить обновления",
["{0} of {1} tasks completed"] = "Выполнено задач: {0} из {1}",
```

The left side is the **exact English text shown in the app**. It's the key, so never change it in a language file. The right side is your translation. The English must match exactly, including capital letters, `…`, `·` and double spaces. If a key is missing or its translation is empty, the app shows the English text instead.

Only the app's own text is translated. Song titles, lyrics, notes, file names, chat messages and AI replies always stay in their original language.

## Translation rules

- Keep placeholders such as `{0}` and `{1}` exactly as they are. You can move them within the sentence: `"할 일 {1}개 중 {0}개 완료"` is fine. The self-test checks that every translation uses the same placeholders as the English.
- Don't translate product and service names: NabyScape, Naby, Naby AI, Hermes, Windows, GitHub, Spotify, LRCLIB, Open-Meteo, Claude, Telegram, SHA-256, JSON, API, URL.
- Keep key names such as `Ctrl+F`, `Shift+Enter` and `Escape` unless your language's Windows uses its own names (Spanish uses `Mayús+Intro`).
- Keep it short. Sidebar labels and buttons have limited room, so prefer the shortest natural wording. Long text in a sidebar label ends in "…".
- Use your language's own quotation marks and decimal separator, for example «…» and `0,1` in Russian and Ukrainian.
- Use the usual Windows tone for your language: polite imperatives (Russian «Выберите», Ukrainian «Виберіть», Korean «선택하세요»).
- Use the same word for the same thing everywhere. See the glossary below.

### Glossary

| English | Español | Bahasa Indonesia | Русский | Українська | 한국어 |
| --- | --- | --- | --- | --- | --- |
| Settings | Ajustes | Pengaturan | Настройки | Налаштування | 설정 |
| Dock / island | Dock / isla | Dock / island | Док | Док | 독 / 아일랜드 |
| Widgets | Widgets | Widget | Виджеты | Віджети | 위젯 |
| Notifications / alerts | Notificaciones / avisos | Notifikasi | Уведомления / оповещения | Сповіщення | 알림 |
| Shortcuts (apps) | Accesos directos | Pintasan | Ярлыки | Ярлики | 바로 가기 |
| Keyboard shortcut | Atajo de teclado | Pintasan keyboard | Сочетание клавиш | Сполучення клавіш | 단축키 |
| Focus | Concentración | Fokus | Фокус / фокусировка | Фокус / фокусування | 집중 |
| Quiet hours | Horas de silencio | Jam tenang | Тихие часы | Тихі години | 방해 금지 시간 |
| Preview | Vista previa | Pratinjau | Предпросмотр | Попередній перегляд | 미리 보기 |
| AI companion | Asistente de IA | Asisten AI | ИИ-помощник | ШІ-помічник | AI 도우미 |
| Provider | Proveedor | Penyedia | Провайдер | Провайдер | 제공업체 |

## Improve an existing translation

1. Open the language file, for example `src/NabyScape/UI/Shared/LocalizationCatalog.uk.cs`.
2. Change only the text on the right of `=`.
3. Open a pull request. If you'd rather not use Git, open an issue that lists the English text, the current translation and your suggestion.

## Add a new string (maintainers)

1. Write the English text in code through the localizing helpers, for example `UI.Caption("Show seconds")`, `UI.CaptionButton("Show seconds", …)` or `Localization.Text("Show seconds")`. Never pass user content through them.
2. Add the same key to **every** language file, at the end of the list:

   ```csharp
   // LocalizationCatalog.es.cs
   ["Show seconds"] = "Mostrar segundos",
   // LocalizationCatalog.id.cs
   ["Show seconds"] = "Tampilkan detik",
   // LocalizationCatalog.ru.cs
   ["Show seconds"] = "Показывать секунды",
   // LocalizationCatalog.uk.cs
   ["Show seconds"] = "Показувати секунди",
   // LocalizationCatalog.ko.cs
   ["Show seconds"] = "초 표시",
   ```

3. Run the self-test. If any language is missing the key or uses different placeholders, the failure names the language and the string.

`python tools/list-ui-captions.py` lists literal captions in the code that have no catalog entry yet and still appear in English.

## Add a new language

1. Copy `LocalizationCatalog.es.cs` to `LocalizationCatalog.<code>.cs`, using the two-letter ISO 639-1 code (for example `de`).
2. In the new file, rename the dictionary (`Spanish` → `German`), update the header comment and translate every value.
3. Add one row to `Languages` in `LocalizationCatalog.cs`, using the language's own name for itself:

   ```csharp
   ("de", "Deutsch", German),
   ```

4. Add the code and native name to the two expected lists in the language test in `src/NabyScape/Tests/ProductionFoundationTests.cs`.
5. Check the script renders. The bundled Manrope font and Segoe UI cover Latin, Cyrillic and Greek. Korean falls back to Malgun Gothic. Other scripts, such as Chinese, Japanese, Arabic or Devanagari, need a Windows font added to the fallback lists. Those lists are `SettingsTheme.Display` and `SettingsTheme.Body`, `Theme.xaml`, `OverlayWindow`, `WelcomeWindow` and `SettingsScene`. Right-to-left layout isn't supported yet.

## Test your translation

Build with the .NET 10 SDK on Windows (see the README), then:

```powershell
# Logic tests: every key translated, placeholders match. Look for the RESULT line.
./artifacts/NabyScape.exe --self-test --report ./outputs/report.txt

# Render Settings in every language in an isolated profile. Your own settings aren't touched.
# Saves production-settings-*.png screenshots and checks that sidebar labels and buttons aren't cut off.
./artifacts/NabyScape.exe --diagnostics ./outputs/languages --isolated-diagnostics --production-ui-only
```

To try it in the app, open **Settings → General → Language & accessibility**, pick a language and choose **Save language and accessibility**. Then quit NabyScape from the tray icon and reopen it.
