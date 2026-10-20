# AOT-Fähigkeit und Speicher

Veröffentlicht wird framework-abhängig, ohne NativeAOT. Beide Rechner brauchen die .NET 10 Desktop Runtime, die [Einstellungen](einstellungen.md) setzen sie als WPF-App ohnehin voraus.

## AOT-fähig halten

- Host und Client setzen `EnableAotAnalyzer` und `EnableTrimAnalyzer`, Core setzt `IsAotCompatible`. Damit meldet schon der normale Build alles, was einem späteren AOT-Publish im Weg stünde.
- Regeln für [Host](host.md) und [Client](client.md):
  - keine reflection-lastigen Bibliotheken,
  - kein WinForms und kein WPF,
  - JSON nur mit Source-Generator.

## Was AOT bräuchte

- `<PublishAot>true</PublishAot>` in Host und Client.
- Den MSVC-Linker aus der Visual-Studio-Workload „Desktopentwicklung mit C++“ auf dem Build-Rechner. NativeAOT kompiliert zu einer `.obj`, die erst der Plattform-Linker zur exe bindet, zusammen mit den Windows-Importbibliotheken und der C-Laufzeit. Ohne Linker bricht der Publish schon vor der AOT-Analyse ab.
- **Prüfung ohne Linker:** Ein getrimmter Publish (`-p:PublishTrimmed=true --self-contained`) meldet die Trim-Warnungen. Am 2026-10-20 gab es dabei weder für den Client mit Hosting und WindowsServices noch für den Host eine einzige.

## Speicher ohne AOT

Gemessen am 2026-10-20 nach vier Sekunden Leerlauf:

| Prozess | Arbeitsspeicher | davon privat | Größe |
|---|---|---|---|
| Minimaler Dauerprozess, framework-abhängig | 22 MB | 6 MB | 186 KB |
| Minimaler Dauerprozess, self-contained getrimmt | 25 MB | 10 MB | 20 MB |
| Client-Dienst, framework-abhängig | 34 MB | 9 MB | 2,6 MB |
| Client-Dienst, self-contained getrimmt | 33 MB | 13 MB | 22 MB |

- Ohne Concurrent GC und ohne PGO sinkt der Bedarf um weniger als 1 MB.
- Der Arbeitsspeicher enthält die DLLs der .NET-Laufzeit, die sich alle .NET-Prozesse teilen; aussagekräftig ist der private Anteil.
