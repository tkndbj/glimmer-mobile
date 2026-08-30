var t = System.Type.GetType("GlimmerGrove.Tests.BudVectorTests, GlimmerGrove.Tests");
if (t == null) return "fixture type not found";
var inst = System.Activator.CreateInstance(t);
var sb = new System.Text.StringBuilder();
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
{
    if (m.GetCustomAttributes(typeof(NUnit.Framework.TestAttribute), true).Length == 0) continue;
    try { m.Invoke(inst, null); sb.Append("+ ").Append(m.Name).Append("\n"); }
    catch (System.Exception e)
    {
        var inner = e.InnerException ?? e;
        sb.Append("X ").Append(m.Name).Append(": ").Append(inner.Message.Substring(0, System.Math.Min(300, inner.Message.Length))).Append("\n");
    }
}
return sb.ToString();
