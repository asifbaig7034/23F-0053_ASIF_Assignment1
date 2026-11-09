using System.Threading;
using System.Threading.Tasks;

namespace MarksApi.Data;

public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}