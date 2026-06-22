namespace Instrumind.Common.Portable
{
    internal static class PortableHash
    {
        public static int Combine(params object[] values)
        {
            unchecked
            {
                var hash = 17;

                foreach (var value in values)
                    hash = (hash * 31) + (value == null ? 0 : value.GetHashCode());

                return hash;
            }
        }
    }
}
