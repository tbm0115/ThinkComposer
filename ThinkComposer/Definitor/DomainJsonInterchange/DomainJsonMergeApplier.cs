// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Thin applier facade for Domain JSON safe merge apply.
// -------------------------------------------------------------------------------------------

using Instrumind.ThinkComposer.MetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonMergeApplier
    {
        public static DomainJsonImportReport Apply(Domain TargetDomain, DomainJsonDocument Document, DomainJsonImportReport ExistingReport = null)
        {
            return DomainJsonImporter.Apply(TargetDomain, Document, ExistingReport);
        }
    }
}
