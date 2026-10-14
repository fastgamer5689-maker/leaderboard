using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameStore>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// The entire website is embedded in this C# file.
app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>NFC Game Arena</title>
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"></script>
<style>
body{margin:0;padding:24px;background:#10131c;color:#fff;font-family:Arial,sans-serif}
main{max-width:850px;margin:auto}
h1{color:#8b9cff}
section{background:#1c2232;padding:20px;border-radius:12px;margin:18px 0}
input,button,select{padding:12px;margin:4px;border-radius:7px;font-size:16px}
input,select{background:#10131c;color:white;border:1px solid #444}
button{background:#6679ff;color:white;border:0;cursor:pointer}
.item{padding:13px;border-bottom:1px solid #394158}
.muted{color:#aab3c8}
.score{float:right;color:#9cffc0}
</style>
</head>
<body>
<main>
<h1>🏆 GAME ARENA</h1>
<p id="status" class="muted">Connecting...</p>

<section>
<h2>Join the queue</h2>
<form id="joinForm">
<input id="name" maxlength="40" placeholder="Enter your name" required>
<button>Join queue</button>
</form>
<p id="message"></p>
</section>

<section>
<h2>🎮 Now playing</h2>
<h3 id="current">Nobody is playing</h3>
</section>

<section>
<h2>⏳ Waiting queue</h2>
<ol id="queue"></ol>
</section>

<section>
<h2>🔧 Game controls</h2>
<p class="muted">Operator controls — secure these before public deployment.</p>
<button id="start">Start next player</button>
<form id="finishForm">
<input id="points" type="number" min="0" step="1" value="0" required>
<select id="won">
<option value="true">Won</option>
<option value="false">Lost</option>
</select>
<button>Save result</button>
</form>
<p id="adminMessage"></p>
</section>

<section>
<h2>🏆 Live leaderboard</h2>
<div id="leaderboard"></div>
</section>

<section>
<h2>📋 Recent results</h2>
<div id="results"></div>
</section>
</main>

<script>
const el = id => document.getElementById(id);
let state = null;

function safe(value) {
 return String(value ?? "").replace(/[&<>"']/g, c =>
  ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"})[c]);
}

async function post(url, data = {}) {
 const response = await fetch(url, {
  method:"POST",
  headers:{"Content-Type":"application/json"},
  body:JSON.stringify(data)
 });
 if (!response.ok) throw Error(await response.text());
 return response.json();
}

function render(s) {
 state = s;
 el("current").textContent = s.current?.name || "Nobody is playing";

 el("queue").innerHTML = s.queue.map(p =>
  `<li>${safe(p.name)}</li>`).join("") || "<li>Queue is empty</li>";

 el("leaderboard").innerHTML = s.leaderboard.map((p,i) =>
  `<div class="item"><b>#${i+1} ${safe(p.name)}</b>
  <span class="score">${p.score} points</span>
  <p class="muted">${p.wins} wins · ${p.games} games</p></div>`
 ).join("") || "<p>No scores yet.</p>";

 el("results").innerHTML = s.results.slice().reverse().slice(0,10).map(r =>
  `<div class="item">${safe(r.name)} — ${r.score} points — ${r.won?"Won":"Lost"}</div>`
 ).join("") || "<p>No results yet.</p>";
}

async function refresh() {
 const r = await fetch("/api/state");
 if (!r.ok) throw Error("Could not load state");
 render(await r.json());
}

const connection = new signalR.HubConnectionBuilder()
 .withUrl("/live")
 .withAutomaticReconnect()
 .build();

connection.on("Update", render);
connection.onreconnected(refresh);

el("joinForm").onsubmit = async e => {
 e.preventDefault();
 try {
  await post("/api/join", {name:el("name").value.trim()});
  el("message").textContent = "You're in the queue!";
  el("name").value = "";
 } catch(err) { el("message").textContent = err.message; }
};

el("start").onclick = async () => {
 try {
  await post("/api/start");
  el("adminMessage").textContent = "Player started!";
 } catch(err) { el("adminMessage").textContent = err.message; }
};

el("finishForm").onsubmit = async e => {
 e.preventDefault();
 if (!state?.current) {
  el("adminMessage").textContent = "Nobody is playing.";
  return;
 }
 const score = Number(el("points").value);
 if (!Number.isSafeInteger(score) || score < 0) {
  el("adminMessage").textContent = "Enter a valid score.";
  return;
 }
 try {
  await post("/api/finish", {
   score,
   won:el("won").value === "true"
  });
  el("adminMessage").textContent = "Score saved!";
 } catch(err) { el("adminMessage").textContent = err.message; }
};

async function init() {
 try {
  await connection.start();
  el("status").textContent = "🟢 Connected — live updates enabled";
  await refresh();
 } catch(err) {
  el("status").textContent = "Connection failed. Refresh to retry.";
 }
}
init();
</script>
</body>
</html>
""", "text/html"));

app.MapHub<LeaderboardHub>("/live");

app.MapGet("/api/state", (GameStore store) => store.GetState());

app.MapPost("/api/join", async (
    JoinRequest request,
    GameStore store,
    IHubContext<LeaderboardHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) ||
        request.Name.Length > 40)
        return Results.BadRequest("Enter a valid name.");

    var player = store.Join(request.Name.Trim());

    await hub.Clients.All.SendAsync("Update", store.GetState());
    return Results.Ok(player);
});

app.MapPost("/api/start", async (
    GameStore store,
    IHubContext<LeaderboardHub> hub) =>
{
    if (!store.StartNext())
        return Results.BadRequest(
            "A player is already playing or the queue is empty.");

    await hub.Clients.All.SendAsync("Update", store.GetState());
    return Results.Ok(new { success = true });
});

app.MapPost("/api/finish", async (
    FinishRequest request,
    GameStore store,
    IHubContext<LeaderboardHub> hub) =>
{
    if (request.Score < 0)
        return Results.BadRequest("Score cannot be negative.");

    if (!store.Finish(request.Score, request.Won))
        return Results.BadRequest("No active player.");

    await hub.Clients.All.SendAsync("Update", store.GetState());
    return Results.Ok(new { success = true });
});

app.Run();

public sealed class LeaderboardHub : Hub { }

public record JoinRequest(string Name);
public record FinishRequest(int Score, bool Won);
public record Player(Guid Id, string Name);
public record GameResult(string Name, int Score, bool Won);
public record Entry(string Name, int Score, int Wins, int Games);
public record GameState(
    List<Player> Queue,
    Player? Current,
    List<Entry> Leaderboard,
    List<GameResult> Results);

public sealed class GameStore
{
    private readonly object gate = new();
    private readonly Queue<Player> queue = new();
    private readonly List<GameResult> results = new();
    private Player? current;

    public Player Join(string name)
    {
        lock (gate)
        {
            var player = new Player(Guid.NewGuid(), name);
            queue.Enqueue(player);
            return player;
        }
    }

    public bool StartNext()
    {
        lock (gate)
        {
            if (current != null || queue.Count == 0)
                return false;

            current = queue.Dequeue();
            return true;
        }
    }

    public bool Finish(int score, bool won)
    {
        lock (gate)
        {
            if (current == null)
                return false;

            results.Add(new GameResult(current.Name, score, won));
            current = null;
            return true;
        }
    }

    public GameState GetState()
    {
        lock (gate)
        {
            var leaderboard = results
                .GroupBy(r => r.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => new Entry(
                    g.Key,
                    g.Sum(r => r.Score),
                    g.Count(r => r.Won),
                    g.Count()))
                .OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.Wins)
                .ToList();

            return new GameState(
                queue.ToList(),
                current,
                leaderboard,
                results.ToList());
        }
    }
}