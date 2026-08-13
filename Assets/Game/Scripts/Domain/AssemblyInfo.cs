using System.Runtime.CompilerServices;

// The file bridge — LoadFrom and WriteInto on each save section, and the DTO
// conversions on a currency ledger — is internal because nothing in the game should
// call it. It is also exactly where a migration bug would live, and an untested
// migration is the one defect that destroys a player's account rather than annoying
// them. The test assembly gets to reach it; nothing else does.
[assembly: InternalsVisibleTo("GlimmerGrove.Tests")]
