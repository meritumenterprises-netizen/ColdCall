using AdjustCeidgAddress.Data;
using Microsoft.EntityFrameworkCore;

namespace AdjustCeidgAddress;

// Serialize lookup creation without serializing HTTP calls or company updates.
public sealed class LocationResolver(Func<FirmyTempDbContext> createContext) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<(Type, string), int> ids = new();

    public async Task<int> ResolveAsync<T>(string name, CancellationToken token = default) where T : class, new()
    {
        await gate.WaitAsync(token);
        try
        {
            var key = (typeof(T), name);
            if (ids.TryGetValue(key, out var id))
                return id;

            await using var context = createContext();
            var existingId = await context.Set<T>().AsNoTracking()
                .Where(row => EF.Property<string>(row, "Name") == name)
                .Select(row => (int?)EF.Property<int>(row, "Id")).FirstOrDefaultAsync(token);
            if (existingId.HasValue)
                id = existingId.Value;
            else
            {
                var entity = new T();
                context.Entry(entity).Property("Name").CurrentValue = name;
                context.Add(entity);
                await context.SaveChangesAsync(token);
                id = context.Entry(entity).Property<int>("Id").CurrentValue;
            }
            ids.Add(key, id);
            return id;
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
