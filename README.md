# Invincible VS Audio Manager

Aplicativo WPF (.NET 6) para visualizar e substituir os arquivos `.ubulk` empacotados dentro
do contêiner `TagFighter-Windows.ucas` do jogo *Invincible VS*.

A ferramenta consome o JSON gerado pelo *matcher* (que mapeia cada `.ubulk` a um par
`offset_start`/`offset_end` dentro do `.ucas`) e permite que o usuário aponte um arquivo
`.wem` para sobrescrever cada slot. Os bytes são gravados **in-place** no `.ucas`,
preservando exatamente o tamanho original do slot:

- se o `.wem` for **maior** que o slot, ele é **truncado**;
- se o `.wem` for **menor**, o restante é preenchido com `0x00` ("00 00 ...").

Isso garante que offsets subsequentes continuem válidos.

## Como usar

1. Coloque o `mapping.json` (do matcher) e o `TagFighter-Windows.ucas` na mesma pasta do
   executável (opcional — o app também tenta carregar de outras localizações).
2. Abra **Invincible VS Audio Manager**. O JSON encontrado na pasta do programa é carregado
   automaticamente; caso contrário use **Carregar JSON...**.
3. Se o `.ucas` não estiver junto do JSON, clique em **Selecionar UCAS...**.
4. Use o filtro para localizar o `.ubulk` desejado e clique em **Substituir...** para
   apontar o `.wem` substituto. O nome do `.wem` aparece na coluna *Substituto*.
5. Repita para quantos arquivos forem necessários (a lista é virtualizada e suporta
   milhares de entradas sem travar).
6. Quando terminar, clique em **Salvar alterações no UCAS** para gravar todas as
   substituições marcadas no arquivo `.ucas`.

> Recomendação: faça backup do `.ucas` antes de aplicar substituições.

## Build

```
dotnet build "Invincible VS Audio Manager.csproj"
```

Requer o SDK do .NET 6 com suporte a WPF (Windows). Em sistemas não-Windows é possível
compilar (graças a `EnableWindowsTargeting=true`), mas a execução continua sendo
exclusivamente Windows.
