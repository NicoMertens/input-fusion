# Netzwerk und Sicherheit

Verbindung zwischen [Host](host.md) auf dem PC und dem Dienst des [Clients](client.md) auf dem Notebook.

- **Der Client verbindet sich zum Server.** Das Notebook wechselt die Netze, der PC steht fest. So braucht nur der PC eine Firewall-Regel für eingehende Verbindungen.
- **Port 15111/TCP**, ein einziger Port für alles. Bewusst nicht 15100/15101 des [Vorbilds](vorbild.md): Solange Mouse Without Borders aus den PowerToys auf denselben Rechnern läuft, hält es diese Ports, und `localhost` landete sonst bei ihm statt beim Host. Später lässt sich der Port in den [Einstellungen](einstellungen.md) von Host und Client ändern.
- **TCP mit `NoDelay`**, Frames aus Länge, Typ und Nutzlast. Bei Stau, etwa im WLAN, werden aufeinanderfolgende Mausbewegungen zusammengefasst.
- **`SslStream`** mit selbstsigniertem Zertifikat je Seite. Kopplung einmalig per Code, danach ist der Zertifikats-Fingerprint fest hinterlegt (Pinning). Das ist sauberer als der gemeinsame Schlüssel des [Vorbilds](vorbild.md).
- **Heartbeat und automatische Neuverbindung** nach Standby oder Netzwechsel. Beide Seiten schicken jede Sekunde einen `Heartbeat` (`MessageConnection.MonitorAsync`). Kommt drei Sekunden lang nichts an, gilt die Gegenseite als weg; jede Nachricht zählt, nicht nur Heartbeats.
  - **Host:** beendet den Remote-Modus, gibt die Maus an der rechten Kante frei und lauscht wieder. Alle Sendungen hängen am selben Abbruch-Token, damit auch ein Senden abbricht, das im vollen Puffer hängt.
  - **Client:** Dienst, Durchstich-Client und [Simulator](simulator.md) senden Heartbeats, sonst trennt der Host sie. Dienst und Simulator verbinden sich neu, wenn der Host schweigt; der Dienst reicht Heartbeats nicht an den Injector weiter. Die Neuverbindung steht unter [Client](client.md#dienst).
  - **Warum TCP-Keepalive nicht reicht:** Es greift nur, solange keine Daten unterwegs sind. Geht das Notebook in den Standby, während die Maus drüben ist, schickt der Host weiter Bewegungen. Sie füllen erst den Sendepuffer, dann entscheidet die TCP-Wiederholung, und bis die aufgibt, verschluckt der Host alle Eingaben. So beobachtet: Die Maus kam nicht zurück. Keepalive bleibt trotzdem an (`FromTcp`: 5 s Ruhe, dann 3 Proben im Abstand von 2 s) für den Fall, dass eine Seite nur liest.
  - Verifiziert: Unit-Tests über TCP auf localhost (schweigendes Gegenüber nach gut 3 s erkannt, antwortendes nicht), der Host trennt einen schweigenden Client nach 3,2 s und lauscht wieder, der Simulator bleibt durchgehend verbunden, der Client verbindet sich nach 3,1 s neu, wenn der Host schweigt.
- **Server finden:** Der Rechnername des PCs wird über DNS aufgelöst (`Dns.GetHostEntry`), wie beim [Vorbild](vorbild.md). Zusätzlich kann der Nutzer eine feste IP eintragen, die ergänzend zum DNS-Ergebnis versucht wird. Keine automatische Suche per Broadcast; der PC steht fest, das genügt.

Die Nachrichten und das Framing liegen in `InputFusion.Core`. Dasselbe Framing nutzt auch die [IPC](ipc.md).
