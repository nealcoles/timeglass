using Timeglass.DevHost;

// dotnet run --project tools/Timeglass.DevHost [-- --port 5400 --seed 7 --workdays 10 --database demo.db --paused true]
await DevHost.Build(args).RunAsync();
