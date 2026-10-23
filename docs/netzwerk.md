# Netzwerk und Sicherheit

Verbindung zwischen [Host](host.md) auf dem PC und dem Dienst des [Clients](client.md) auf dem Notebook.

- **Der Client verbindet sich zum Server.** Das Notebook wechselt die Netze, der PC steht fest. So braucht nur der PC eine Firewall-Regel für eingehende Verbindungen.
- **Port 15111/TCP**, ein einziger Port für alles. Bewusst nicht 15100/15101 des [Vorbilds](vorbild.md): Solange Mouse Without Borders aus den PowerToys auf denselben Rechnern läuft, hält es diese Ports, und `localhost` landete sonst bei ihm statt beim Host. Später lässt sich der Port in den [Einstellungen](einstellungen.md) von Host und Client ändern.
- **TCP mit `NoDelay`**, Frames aus Länge, Typ und Nutzlast. Bei Stau, etwa im WLAN, werden aufeinanderfolgende Mausbewegungen zusammengefasst.
- **`SslStream`** mit selbstsigniertem Zertifikat je Seite. Kopplung einmalig per Code, danach ist der Zertifikats-Fingerprint fest hinterlegt (Pinning). Das ist sauberer als der gemeinsame Schlüssel des [Vorbilds](vorbild.md).
- **Heartbeat und automatische Neuverbindung** nach Standby oder Netzwechsel.
- **Server finden:** Der Rechnername des PCs wird über DNS aufgelöst (`Dns.GetHostEntry`), wie beim [Vorbild](vorbild.md). Zusätzlich kann der Nutzer eine feste IP eintragen, die ergänzend zum DNS-Ergebnis versucht wird. Keine automatische Suche per Broadcast; der PC steht fest, das genügt.

Die Nachrichten und das Framing liegen in `InputFusion.Core`. Dasselbe Framing nutzt auch die [IPC](ipc.md).
