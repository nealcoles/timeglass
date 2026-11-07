using Timeglass.DevHost;

// dotnet run --project tools/Timeglass.DevHost [-- --port 5400 --seed 7 --workdays 10 --database demo.db --paused true --now 2026-11-05T13:00:00Z --timeZone Europe/Oslo --web ../../web/dist]
await DevHost.Build(args).RunAsync();
