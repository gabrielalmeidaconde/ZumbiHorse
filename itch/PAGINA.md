# Página do itch.io

Tudo o que vai na página https://gabrielalmeidaconde.itch.io/ultimate-zombi-horse (em **Edit game**).
As imagens estão nesta pasta:

- `capa.png` (630 x 500): montada com os sprites do próprio jogo (exportados do `Art.cs`) e o título no verde do lobby.
- `corrida.gif` (800 x 450, 10 s, 1,4 MB): fim do posicionamento e corrida na Estação Fantasma.
- `prints/` (1920 x 1080): lobby, caixa de itens, posicionamento, uma corrida em cada mapa, placar e vitória.

Para gerar os prints de novo (depois de mudar o visual), rode o build com o modo demo, trocando o mapa (`-zumbiMap 0` a `4`).
Os prints saem do que o jogo desenha (não da tela), então overlays do sistema não aparecem:

```
ZumbiHorse.exe -screen-fullscreen 1 -screen-width 1920 -screen-height 1080 -zumbiDemo -zumbiMute -zumbiBots 6 -zumbiDiff mix -zumbiRounds 2 -zumbiMap 0 -zumbiShots prints
```

O GIF foi gravado da tela (ffmpeg `gdigrab` com `-draw_mouse 0`) durante uma partida demo e convertido com
`palettegen`/`paletteuse` sem dithering (para a pixel art ficar nítida).

## Campos da página

| Campo | O que colocar |
|---|---|
| Title | Ultimate Zumbi Horse |
| Short description or tagline | Monte a fase, arme as armadilhas e corra até o cérebro antes dos outros zumbis. De 2 a 8 jogadores, no mesmo PC ou online. |
| Classification | Games |
| Kind of project | HTML (o build `html5` roda na página; os outros ficam para baixar) |
| Release status | Released (ou In development enquanto estiver testando) |
| Genre | Platformer |
| Tags | `party-game`, `local-multiplayer`, `multiplayer`, `zombies`, `pixel-art`, `2d`, `funny`, `level-editor`, `controller-support`, `online-multiplayer` |
| Cover image | `capa.png` (630 x 500) |
| Screenshots | `corrida.gif` primeiro, depois os prints de `prints/` na ordem dos números |
| Embed options (html5) | 1280 x 720, **Fullscreen button** ligado, **Mobile friendly** desligado |
| Inputs | Keyboard, Mouse, Xbox controller, Gamepad (any) |
| Multiplayer | Local multiplayer e Server-based networked multiplayer, **Player count** 2 a 8 |
| Languages | Portuguese (Brazil) |
| Accessibility | Configurable controls: não. Interactive tutorial: não |
| Visibility | Draft até conferir tudo; depois Public |

## Descrição

Cole o texto abaixo no campo **Description** (o editor do itch aceita colar texto formatado; se as tabelas
não ficarem boas, troque cada tabela por uma lista).

---

**Ultimate Zumbi Horse** é uma corrida de plataforma para 2 a 8 zumbis, inspirada em *Ultimate Chicken Horse*.
Cada rodada, cada um escolhe um item, coloca na fase e depois todos correm até o **cérebro**. Os itens ficam
de uma rodada para a outra: a fase vai virando uma armadilha cada vez maior, e o segredo é deixá-la difícil
o bastante para os outros, mas não impossível para você.

Jogue no mesmo computador (teclado e controles, até 8 juntos), contra bots ou **online com um código de sala**,
inclusive aqui no navegador.

### Como funciona uma rodada

1. **Caixa de itens**: cada jogador escolhe um item com o seu cursor (20 segundos).
2. **Posicionamento**: todos colocam o item ao mesmo tempo: blocos para ajudar, armadilhas para atrapalhar.
3. **Corrida**: todos correm da largada até o cérebro. Quem morre fica fora da rodada.

### Pontuação

| Situação | Pontos |
|---|---|
| Chegar no cérebro | +2 |
| Ser o primeiro a chegar | +1 |
| Outro zumbi morrer na sua armadilha | +1 por zumbi (só vale se alguém chegou) |
| **Todos** chegaram | ninguém pontua ("Fácil demais!") |
| Ninguém chegou | ninguém pontua (e a próxima caixa traz uma bomba) |

Quem fizer **10 pontos** primeiro vence.

### Controles

| Ação | Teclado WASD | Teclado Setas | Controle |
|---|---|---|---|
| Andar / mover o cursor | A / D (W / S no cursor) | Setas | Analógico ou D-pad |
| Pular | W ou Espaço | Seta para cima | A |
| Confirmar (entrar, pronto, pegar, colocar) | Espaço | Enter | A |
| Girar o item | Q / E | Ctrl / Shift direitos | LB / RB |
| Trocar o mapa (no lobby) | A / D | Esquerda / Direita | Analógico ou D-pad |
| Voltar | Q | Backspace | B |
| Pausar | Esc | Esc | Start |
| Dançar | F | . | X |

No lobby: **W / S** colocam e tiram bots, **E** muda a dificuldade, **O** abre o jogo online e **C** abre o criador de fases.
No navegador, clique no jogo antes de jogar para ele receber o teclado.

### O que tem no jogo

- **17 itens**: caixa, prancha, pedra, gelo, caixa podre, esteira, mola, ventilador, portais, espinhos, serra,
  serra móvel, canhão de ossos, lança-chamas, bomba, plataforma móvel e gangorra.
- **5 mapas**: Cemitério, Estação Fantasma (com um trem fantasma), Serraria, Pântano Ácido e Ferrovia Dupla.
- **Bots** em três dificuldades, que montam um mapa da fase, simulam os pulos e desviam dos perigos.
- **Online com código de sala**: crie a sala, passe o código e jogue com amigos em outros computadores
  (ou na rede local pelo IP).
- **Criador de fases**: monte a sua fase, passe nela e mande o **código do desafio** para os amigos tentarem
  bater o seu tempo.
- **26 danças** para comemorar (ou provocar).
- Pixel art, efeitos e músicas gerados por código.

### Créditos

Feito em Unity por Gabriel Almeida Conde, Luisa Farah, Matheus Luciano e Yuri Henrique, na disciplina de Jogos do Insper.

---

*English:* a party platformer for 2 to 8 zombies inspired by *Ultimate Chicken Horse*. Pick an item, place it
on the level, then race to the brain. Local (keyboard + controllers), bots, and online play with a room code,
right here in the browser. The game is in Portuguese.
