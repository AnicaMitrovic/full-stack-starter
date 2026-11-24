# Säkerhet vecka 3: sex hål, bevisade och lagade

Varje hål följer samma kedja: **missbruksberättelse → curl-bevis → rött test → minsta lagning → grönt.**
Testerna ligger i `backend/Starter.Tests/Security/` och är märkta `[Trait("Abuse", "MB-x.y")]`.

Kör alla säkerhetstester: `dotnet test backend/Starter.Tests --filter "Abuse~MB"`

> Allt nedan är kört **lokalt** mot `http://localhost:5069`. Angrip aldrig något du inte äger.

| Id | Hål | Bevis före (curl) | Före | Efter | Test |
|---|---|---|---|---|---|
| MB-1.1 | IDOR: enskild uppgift filtrerades inte på team | `curl -H "X-Team: salj" http://localhost:5069/api/todos/202` | `200` + Ekonomis "Bonusunderlag ledningsgrupp (konfidentiellt)" | `404` | `TeamIsolationTests` |
| MB-2.1 | Mass assignment: `teamId` / `isDeleted` i DTO:n | `curl -H "X-Team: salj" -H "Content-Type: application/json" -d '{"title":"planterad","teamId":2}' http://localhost:5069/api/todos` | Uppgiften dök upp i Ekonomis lista | Hamnar i Säljs lista, fälten ignoreras | `MassAssignmentTests` |
| MB-3.1 | SQL-injektion i sökningen (`FromSqlRaw` + interpolering) | `curl -H "X-Team: salj" --get --data-urlencode "q=' OR 1=1 --" http://localhost:5069/api/todos/search` | Alla fyra teamens uppgifter | `[]` | `SearchInjectionTests` |
| MB-4.1 | Stacktrace till klienten i alla miljöer | `curl -H "X-Team: salj" --get --data-urlencode "q='" http://localhost:5069/api/todos/search` | `500` med `SqliteException`, SQL-fel och full stacktrace | Utanför Development: kort ProblemDetails, felet i loggen | `ErrorDisclosureTests` |
| MB-5.1 | CORS tillät alla webbplatser | `curl -i -H "Origin: https://evil.example" -H "X-Team: ekonomi" http://localhost:5069/api/todos` | `Access-Control-Allow-Origin: *` | Ingen CORS-header; bara `http://localhost:5173` tillåts | `CorsTests` |
| MB-6.1 | Ingen rate limit på sökningen | `for i in $(seq 1 150); do curl -s -o /dev/null -w "%{http_code}\n" -H "X-Team: salj" "http://localhost:5069/api/todos/search?q=a"; done \| sort \| uniq -c` | `150 × 200` | `30 × 200`, sedan `429` + `Retry-After: 60` | `SearchRateLimitTests` |

## Lagningarna i korthet

1. **MB-1.1**: `TodoRepository.FindAsync` filtrerar på anroparens team (och döljer borttagna). Fel team ger 404, inte 403, så det avslöjas inte att id:t finns.
2. **MB-2.1**: `CreateTodoRequest`/`UpdateTodoRequest` har bara `title`, `dueDate`, `priority`. Servern sätter alltid team; borttagen-flaggan tas aldrig från klienten.
3. **MB-3.1**: Sökningen är LINQ med `EF.Functions.Like`, så texten är alltid en parameter. `%` och `_` escapas och söks bokstavligt.
4. **MB-4.1**: `UseDeveloperExceptionPage` bara i Development, annars `UseExceptionHandler()` + `AddProblemDetails`.
5. **MB-5.1**: `WithOrigins(Frontend:Origin)` i stället för `AllowAnyOrigin`.
6. **MB-6.1**: Inbyggda rate limitern, fast fönster per klient-IP (inte per `X-Team`, den kan vem som helst byta), konfigurerbart under `RateLimiting:Search`.
