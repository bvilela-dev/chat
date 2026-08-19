// Usings globais das suítes de teste.
//
// `global using` (C# 10+) aplica o import a todos os arquivos do projeto,
// eliminando o bloco repetido de `using Xunit; using Shouldly; using NSubstitute;`
// no topo de cada arquivo de teste. O ganho é de sinal/ruído: o que sobra no
// topo de um arquivo de teste passa a ser só o que é específico dele.
global using NSubstitute;
global using Shouldly;
global using Xunit;
