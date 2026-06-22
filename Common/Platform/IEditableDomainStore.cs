using System.Threading;
using System.Threading.Tasks;
using Instrumind.Common.Portable;

namespace Instrumind.Common.Platform
{
    public interface IEditableDomainStore
    {
        string GetSidecarPath(string sourcePath);

        Task<EditableDomainModel> TryLoadAsync(string sourcePath, CancellationToken cancellationToken);

        Task SaveAsync(EditableDomainModel domain, CancellationToken cancellationToken);
    }
}
