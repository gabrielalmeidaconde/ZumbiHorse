# Ultimate Zumbi Horse

Versão simples de **Ultimate Chicken Horse** com zumbis, feita em **Unity 6 (6000.3.25f1)**.
Para 2 a 8 jogadores (humanos e bots) no mesmo computador ou **online** (veja [Multiplayer online](#multiplayer-online)).

Cada rodada tem três fases:

1. **Caixa de itens**: cada jogador escolhe um item com o seu cursor (20 s; quem não escolher ganha um item que sobrou).
2. **Posicionamento**: todos colocam o item na fase ao mesmo tempo (blocos para ajudar, armadilhas para atrapalhar).
3. **Corrida**: todos os zumbis correm da largada até o **cérebro**. Quem morre fica fora da rodada.

Os itens ficam na fase de uma rodada para a outra, então ela vai ficando cada vez mais difícil.

## Menu principal, pausa e configurações

O jogo abre no **menu principal**: **Jogar** (lobby), **Jogar online**, **Criar fase**, **Configurações**,
**Controles** e **Sair** (o Sair não aparece no navegador). Qualquer teclado ou controle mexe no menu
(cima/baixo escolhe, CONFIRMAR aperta, VOLTAR volta) e o mouse também. No lobby, **Esc**/**Start** volta para o menu
(no online, para sair da sala use o painel online).

Na partida, **Esc**/**Start** abre o **menu de pausa**: **Continuar**, **Configurações**, **Controles** e
**Sair da partida** (volta para o lobby; no desafio solo, **Sair do desafio** volta para o criador de fases).
VOLTAR ou Esc/Start de novo continuam o jogo. No online quem mexe na pausa é o host (os outros veem o mesmo menu)
e as configurações ficam só no menu principal, porque cada computador tem as suas.

| Configuração | O que muda |
|---|---|
| Volume | De 0 (mudo) a 100%, de 10 em 10 |
| Tela cheia | Tela cheia ou janela de 1280 x 720 (no navegador, use o botão de tela cheia da página) |
| Resolução | As resoluções do monitor (na janela, o tamanho dela) |
| Pontos para vencer | De 3 a 30 (padrão 10) |
| Tremer a tela | Liga/desliga o tremor quando alguém morre ou algo explode |

Esquerda/direita mudam o valor (CONFIRMAR ou clicar passa para o próximo). Tudo fica salvo no computador
(`PlayerPrefs`; a tela cheia e a resolução o próprio Unity guarda). A tela **Controles** mostra a tabela com as
teclas de cada tipo de dispositivo e os atalhos do lobby.

## Bots (jogadores do computador)

No lobby, qualquer jogador controla os bots:

| Ação | Teclado WASD | Teclado Setas | Controle |
|---|---|---|---|
| Colocar um bot | W | Seta para cima | Cima |
| Tirar o último bot | S | Seta para baixo | Baixo |
| Mudar a dificuldade do último bot | E | Shift direito | RB (ou Y) |

Dá para misturar dificuldades (ex.: coloque um bot, deixe-o Difícil, coloque outro e deixe-o Fácil).
Se os 8 lugares estiverem ocupados e um humano apertar CONFIRMAR, ele toma o lugar do último bot.
A partida precisa de pelo menos um humano.

| Dificuldade | Como joga |
|---|---|
| Fácil | Demora para reagir, erra pulos, quase não presta atenção em serras/trens/fogo e coloca itens em qualquer lugar |
| Médio | Reage razoavelmente, erra de vez em quando, espera os perigos passarem e mira as armadilhas mais ou menos no caminho |
| Difícil | Rápido e preciso, desvia de serras, trens, fogo e ossos, pega carona no trem e coloca as armadilhas bem no caminho sem deixar a fase impossível |

Como funciona: cada bot monta um mapa de navegação da fase (onde dá para ficar em pé e quais
pulos funcionam, simulando a mesma física do zumbi), procura o caminho mais curto até o cérebro e
"aperta os botões" como um jogador. Antes de pular, os bots Médio e Difícil simulam o pulo prevendo
onde estarão as serras móveis, os trens e o fogo.

Quando a fase fica impossível (ou eles empacam), os bots não ficam parados para sempre:

- **Sem caminho até o cérebro**: vão até o ponto mais perto dele que conseguem alcançar e tentam um pulo no escuro.
- **Empacados esperando um perigo passar**: depois de alguns segundos param de esperar e arriscam.
- **Sem chegar mais perto do cérebro por muito tempo** (7 s sem caminho, 18 s com caminho): **desistem**
  (o zumbi explode com "DESISTIU!"), para a rodada poder acabar.
- Se um pulo cai num lugar inesperado (vento, esteira, caixa podre...), eles evitam aquele pulo e procuram outro caminho.

## Mapas

No lobby, qualquer jogador troca o mapa com **esquerda/direita** (A/D, setas ou analógico).

| Mapa | O que tem |
|---|---|
| Cemitério | O clássico: três buracos, um paredão e um caminho de plataformas por cima |
| Estação Fantasma | Um trem fantasma vai e volta no fundo do vale (dá para pegar carona no teto; as pontas matam) e pontes por cima |
| Serraria | Serras fixas, uma serra que sobe e desce num pilar, outra que varre o chão, um poço de espinhos e um caminho por cima |
| Pântano Ácido | Ilhas no meio do ácido (uma de gelo), uma serra que sobe e desce e plataformas por cima |
| Ferrovia Dupla | Dois trens em pontes sobre o ácido; a de cima está quebrada (dá para pegar o trem ou pular pela pedra embaixo do buraco) |
| Aleatório | Sorteia um dos mapas quando a partida começa |

Não dá para colocar itens no trilho do trem (área marcada durante o posicionamento).

## Criador de fases e desafios

Escolha **Criar fase** no menu principal ou, no lobby, aperte **C** (teclado), **Select** (controle) ou clique em
**CRIAR FASE** (só no jogo offline).
Dá para montar a fase com qualquer teclado, controle ou mouse:

| Ação | Teclado | Controle | Mouse |
|---|---|---|---|
| Mover o cursor | WASD ou setas | Analógico / D-pad | Apontar |
| Pintar (segure e ande para pintar vários) | Espaço / Enter | A | Botão esquerdo |
| Trocar a peça | Q / E ou Ctrl / Shift direitos | LB / RB | Rodinha ou clicar na barra |
| Apagar | peça "Apagar" | peça "Apagar" | Botão direito |
| Menu (testar, copiar código, tema, sair) | Esc | Start | Botão MENU |

- Peças: terra, pedra, gelo, trilho, ácido, espinhos, serra, serra móvel (marque o começo e o fim),
  largada, cérebro, lápide e árvore. Trilho reto com 7 células ou mais ganha um trem sozinho.
- São 6 fases salvas no computador, e cada mudança é salva na hora.
- Para compartilhar, passe na fase em **TESTAR**: o seu melhor tempo vai junto no código. Depois use
  **COPIAR CÓDIGO DO DESAFIO** e cole o código (começa com `ZH1-`) numa conversa. Qualquer mudança na fase pede
  um teste novo.
- O amigo copia o código e escolhe **COLAR CÓDIGO DE DESAFIO** no criador: é uma corrida solo contra o relógio
  (morreu, volta para a largada e o tempo continua) para tentar bater o tempo de quem criou.
- Na versão de navegador, copiar e colar abre uma caixinha do próprio navegador quando ele não deixa usar a
  área de transferência direto.

## Pontuação

| Situação | Pontos |
|---|---|
| Chegar no cérebro | +2 |
| Ser o primeiro a chegar | +1 |
| Outro zumbi morrer na sua armadilha | +1 por zumbi (só vale se alguém chegou) |
| **Todos** chegaram | ninguém pontua ("Fácil demais!") |
| Ninguém chegou | ninguém pontua |

Quem fizer **10 pontos** primeiro vence (em caso de empate, joga-se mais uma rodada).

Para a partida não travar:

- Quando todos os humanos já morreram ou chegaram, os bots que sobraram têm no máximo **12 segundos** ("SÓ FALTAM OS BOTS!").
- Se **ninguém chegou** na rodada, a próxima caixa de itens sempre traz uma **bomba** para abrir caminho
  (os bots Médio e Difícil sabem mirar a bomba no que está bloqueando a passagem).

## Jogadores e controles

No lobby, cada jogador aperta CONFIRMAR para entrar e CONFIRMAR de novo para ficar pronto.
A partida começa quando pelo menos 2 jogadores estão prontos. Funciona com qualquer combinação:
2 no teclado (WASD + setas), 2 no teclado + 2 controles, 3 ou 4 controles, etc.

| Ação | Teclado WASD | Teclado Setas | Controle |
|---|---|---|---|
| Andar / mover cursor | A/D (W/S no cursor) | Setas | Analógico esquerdo / D-pad |
| Pular | W ou Espaço | Seta para cima | A |
| Confirmar (entrar, pronto, pegar, colocar) | Espaço | Enter | A |
| Girar item (anti-horário / horário) | Q / E | Ctrl direito / Shift direito | LB (ou X) / RB (ou Y) |
| Trocar mapa (no lobby) | A / D | Esquerda / Direita | Analógico / D-pad |
| Voltar / sair do lobby | Q | Backspace | B |
| Pausar | Esc | Esc | Start |
| Dançar (de novo: sorteia outra dança) | F | . (do teclado numérico ou ao lado da vírgula) | X |
| Trocar o visual do zumbi (no lobby) | R | Ctrl direito | LB |

Com o jogo pausado abre o menu de pausa (veja [Menu principal, pausa e configurações](#menu-principal-pausa-e-configurações)).
O zumbi também desliza e pula na parede.
Dá para dançar no lobby, no placar e na corrida (parado no chão). O zumbi dança sem parar até andar, pular ou morrer,
e cada aperto sorteia outra dança das raças do World of Warcraft (26 no total: a masculina e a feminina de cada raça).
Os bots às vezes comemoram dançando no placar quando chegam no cérebro.

No lobby, o cartão de cada jogador mostra os botões dele (ficar pronto, trocar o visual, sair). A tabela com os
controles de cada tipo de dispositivo fica em **Controles**, no menu principal e na pausa (a coluna de quem está
jogando com ele fica destacada com a cor do jogador). Durante a escolha e o posicionamento, cada cursor
mostra os botões daquele jogador, e nas duas primeiras corridas aparece um lembrete de como pular.

## Visuais dos zumbis

No lobby, cada jogador troca o visual do seu zumbi com **R** (teclado WASD), **Ctrl direito** (teclado setas) ou
**LB** (controle); o nome do visual aparece embaixo do zumbi. São 10 visuais, e todos guardam a cor do jogador em
alguma parte para dar para saber quem é quem:

| Visual | Como é | Onde fica a cor do jogador |
|---|---|---|
| Clássico | O zumbi de sempre: cabelo, camisa e calça | Camisa |
| Esqueleto | Caveira, costelas e ossos | Cachecol e olhos brilhando |
| Faraó | Múmia enfaixada com colar de ouro | Listras do toucado de faraó |
| Pirata | Chapéu de três pontas com caveira e tapa-olho | Listras da camisa |
| Astronauta | Capacete de vidro, traje branco e mochila | Painel do peito e cinto |
| Vampiro | Cabelo lambido, presas e terno preto | Capa |
| Palhaço | Careca com chapéu de festa e nariz vermelho | Cabelo e roupa de bolinhas |
| Craque | Faixa na testa e calção branco | Camisa listrada de time |
| Cavalo | Máscara de cavalo | Crina e camisa |
| Punk | Brinco e jaqueta de couro com tachas | Moicano e camiseta |

O visual escolhido fica salvo no computador para aquele lugar (P1, P2...). Os bots ganham um visual sorteado.
No online, cada jogador troca o seu do mesmo jeito, no lobby do host.

## Multiplayer online

Escolha **Jogar online** no menu principal (ou aperte **O** no lobby) para abrir o painel **JOGAR ONLINE**:

- **Criar sala**: o jogo gera um **código** (ex.: `K7QF2M`). Passe para os amigos.
- **Entrar com código**: digite o código e aperte Enter. Funciona pela internet e também na versão do navegador (itch.io).
- **Rede local**: criar sala / entrar pelo IP de quem criou (mesmo Wi-Fi, sem internet; não funciona no navegador).

Quem entra na sala vê o lobby do host e cada teclado ou controle daquele computador entra apertando CONFIRMAR,
como se estivesse no mesmo PC (dá para misturar: 2 no host + 2 em outro computador). No máximo 8 jogadores e
8 computadores. Se alguém cair no meio da partida, o zumbi dele vira CPU (Médio) e a partida continua.

Como funciona: o host roda o jogo de verdade (física, bots, pontuação). Os outros computadores mandam os botões
dos seus dispositivos (60 vezes por segundo) e recebem de volta, 20 vezes por segundo, o que mudou na tela
(cada sprite: qual é, posição, rotação, escala, cor), o estado da interface, os sons e os textos.
Usa uns 10 KB/s por computador conectado.

### Antes de usar o código de sala (uma vez só)

O código usa o **Unity Relay**, um serviço da Unity (grátis para jogos pequenos). Ele só funciona depois que o
projeto é ligado a um projeto do Unity Cloud:

1. Abra o projeto no Unity (6000.3.25f1) já logado na sua conta Unity.
2. **Edit > Project Settings > Services**: em **Organization** escolha a sua organização, marque
   **Create a new cloud project** (ou **Use an existing cloud project** e escolha um) e clique no botão de baixo
   (**Link Unity project to cloud project** / criar projeto). Se a lista de organizações vier vazia, entre na sua
   conta Unity no editor e clique em **Refresh**.
3. Em https://cloud.unity.com, abra o projeto e procure **Relay** (em Multiplayer / Products). Se ele pedir para
   ativar, ative.
4. Gere os builds de novo (menu **Zumbi Horse**). O ID do projeto vai dentro do build, então builds antigos não servem.

Enquanto isso não for feito, "Criar sala" mostra o aviso de que o build não está ligado ao Unity Cloud.
A rede local funciona sem nada disso.

Observações:

- Redes de faculdade e de empresa às vezes bloqueiam o Relay (UDP) ou isolam os aparelhos do Wi-Fi uns dos outros
  (aí a rede local não funciona). A versão do navegador usa WebSocket seguro, que costuma passar nesses bloqueios.
- Host e clientes precisam da mesma versão do jogo (senão o host recusa a conexão e avisa).
- Quem entrou na sala vê o **ping** (tempo de ida e volta até o host) no canto de baixo da tela. Quem não é o host
  sente esse atraso entre apertar um botão e ver o zumbi se mexer, porque a partida roda no computador do host.
  Para ter menos atraso: o host deve ser quem tem a melhor internet; de preferência o jogo baixado (e não o navegador);
  e, no navegador, a aba do jogo precisa ficar visível (o navegador deixa abas escondidas bem mais lentas, e aí o
  jogo fica lento para todo mundo).
- O código de sala usa WebSocket seguro (como um site). Se no jogo baixado não conectar, teste abrir os dois
  jogos com `-relayDtls` (usa UDP, o padrão da Unity). A versão do navegador sempre usa WebSocket.

## Itens

Todos os itens giram (menos a bomba e a gangorra): o giro muda a direção da mola, do canhão, do lança-chamas,
do ventilador, da esteira, da serra móvel e da plataforma móvel, e deixa a prancha ou os portais na vertical/horizontal.

| Item | O que faz |
|---|---|
| Caixa | Bloco 1x1 |
| Prancha | Plataforma 3x1 |
| Pedra | Bloco 2x2 |
| Gelo | Bloco 2x1 escorregadio |
| Caixa Podre | Treme quando alguém pisa, some e volta 2,5 s depois |
| Esteira | Plataforma 3x1 que empurra quem está em cima para um lado |
| Mola | Arremessa o zumbi na direção para onde aponta |
| Ventilador | Sopra os zumbis (4,5 células) na direção para onde aponta |
| Portais | Você coloca dois (verde e roxo): quem entra num sai no outro |
| Espinhos | Mata quem encostar nas pontas |
| Serra | Mata quem encostar |
| Serra Móvel | Serra que vai e volta num trilho de 3 células |
| Canhão de Ossos | Atira um osso a cada 2,2 s durante a corrida |
| Lança-Chamas | Cospe fogo (2,5 células) por 1,1 s, a cada 2,8 s |
| Bomba | Explode tudo numa área 3x3 (não destrói pedra, trilhos e coisas do mapa) |
| Plataforma Móvel | Plataforma de 3 que vai e volta 3 células e carrega quem está em cima. Fica sempre na horizontal; girar escolhe a direção (direita, cima, esquerda, baixo). Ocupa o trajeto inteiro |
| Gangorra | Prancha de 5 sobre um apoio: o lado com mais peso desce e o outro sobe (quanto mais na ponta, mais peso). Dá para arremessar quem está do outro lado |

Não dá para colocar itens nas áreas de largada e de chegada (aparecem marcadas no posicionamento).

## Como abrir e jogar

1. Abra a pasta `ZumbiHorse` pelo Unity Hub (Unity 6000.3.25f1).
2. A cena `Assets/Scenes/Jogo.unity` abre sozinha na primeira vez. Aperte **Play**: o jogo abre no menu principal.
   (O jogo também roda a partir de qualquer cena vazia: o `Bootstrap` cria o `GameManager`.)
3. Para gerar executável: menu **Zumbi Horse > Build Linux**, **Build Windows** ou **Build WebGL (itch.io)**.
   Cada um precisa do módulo daquela plataforma instalado no Unity Hub (Installs > engrenagem > Add modules);
   se faltar, o menu avisa. Os builds vão para a pasta `Builds/` (ela não fica no git).

## Publicar no itch.io

1. Gere os builds (veja acima). O **WebGL** é o melhor para o itch.io: o jogo roda direto na página, sem baixar nada.
   O menu já configura a compressão do jeito que o itch.io aceita (Gzip com "Decompression Fallback").
2. Compacte cada build em um `.zip` (o conteúdo da pasta, não a pasta):
   ```
   cd Builds/WebGL && zip -r ../ZumbiHorse-web.zip . && cd -
   cd Builds/Linux && zip -r ../ZumbiHorse-linux.zip . && cd -
   cd Builds/Windows && zip -r ../ZumbiHorse-windows.zip . && cd -
   ```
3. Em https://itch.io/game/new: **Kind of project = HTML** (se for subir o WebGL) ou **Downloadable**.
4. Em **Uploads**, envie os zips. No zip WebGL marque *"This file will be played in the browser"*;
   nos outros marque a plataforma (Linux / Windows).
5. Em **Embed options** (só WebGL): tamanho **1280 x 720**, marque **Fullscreen button** e
   **Click to launch in fullscreen** se quiser.
6. Escreva a descrição com os controles (a tabela acima), coloque capa e prints, e salve como **Draft** para testar.
   Quando estiver bom, mude **Visibility** para **Public**.

No navegador, clique no jogo antes de jogar (para ele receber o teclado). Controles também funcionam no navegador.

### Publicação automática (GitHub Actions)

O arquivo `.github/workflows/itch.yml` faz isso sozinho a cada push na `main`: compila para navegador, Windows,
Mac e Linux (com o [GameCI](https://game-ci.com)) e envia para o itch.io com o butler, nos canais
`html5`, `windows`, `mac` e `linux`. Leva uns 15 a 30 minutos; o andamento aparece na aba **Actions** do GitHub.

Configuração (uma vez só):

1. Crie a página do jogo no itch.io (como rascunho) e anote o endereço: `usuario.itch.io/nome-do-jogo`.
2. Gere uma chave em https://itch.io/user/settings/api-keys.
3. No GitHub: **Settings > Secrets and variables > Actions**
   - aba **Secrets**: `BUTLER_API_KEY` (a chave do itch), `UNITY_EMAIL` e `UNITY_PASSWORD` (sua conta Unity) e
     `UNITY_LICENSE` (licença do Unity; siga "Personal License" em https://game-ci.com/docs/github/activation);
   - aba **Variables**: `ITCH_GAME` = `usuario/nome-do-jogo`.
4. Depois do primeiro envio, no itch.io (**Edit game > Uploads**), marque o arquivo `html5` como
   *This file will be played in the browser* e ajuste o tamanho para 1280 x 720. Os envios seguintes mantêm isso.

Sem a chave do itch, o workflow só compila (e avisa que não publicou).

O projeto usa o pacote **Input System** (necessário para vários controles ao mesmo tempo).
Se copiar os scripts para outro projeto sem esse pacote, só os dois lados do teclado funcionam.

## Estrutura

Tudo (pixel art, sons, fase e interface) é gerado por código, sem nenhum asset externo.

```
Assets/
  Scenes/Jogo.unity      câmera + GameManager
  Scripts/
    Bootstrap.cs         cria o GameManager se a cena não tiver um
    Core/
      GameInput.cs       InputSource: teclado WASD, teclado setas, controles (Input System)
      Art.cs             pixel art procedural (zumbis e seus visuais, blocos, armadilhas, cenário)
      Sfx.cs             efeitos sonoros sintetizados
      FxParticle.cs      partículas, poeira, explosões
      Clipboard.cs       copiar/colar (no navegador usa Plugins/WebGL/Clipboard.jslib)
      Spinner.cs         gira/balança/desliza objetos
    Game/
      GameManager.cs     fases da partida, pontuação e toda a interface (IMGUI)
      GameManager.Net.cs modo cópia do cliente online, estado da interface pela rede, painel online
      GameManager.Menu.cs    menu principal, menu de pausa, configurações (salvas) e tela de controles
      MapDefs.cs         os mapas, desenhados em texto (legenda no topo do arquivo)
      MapCode.cs         fases criadas: código de compartilhar, espaços salvos e trens automáticos
      GameManager.Editor.cs  criador de fases e desafio solo contra o relógio
      Level.cs           monta a fase a partir do mapa, grade de ocupação, fundo
      Train.cs           trem fantasma (vai e volta, carona no teto, pontas mortais)
      Bots/              IA dos bots: NavGrid (mapa de navegação e simulação de pulos),
                         BotPlayer (escolher, posicionar, correr), BotSystem (roda os bots)
      Zombie.cs          controle de plataforma (pulo variável, coyote time, parede, gelo, molas)
      PlayerCursor.cs    cursor de escolher/posicionar
      GridObject.cs      objetos que ocupam células da grade
      DemoDriver.cs      modo de teste automático com bots (só liga com -zumbiDemo)
    Items/               definições, fábrica e comportamento dos itens
    Net/                 multiplayer online: NetTransport (Unity Transport + Relay ou IP), NetGame (salas,
                         botões remotos, envio do estado), SceneSync (cópia da cena no cliente)
  Editor/ZumbiSetup.cs   menu "Zumbi Horse": recriar cena e builds
```

Para ajustar a física do zumbi, mexa nas constantes no topo de `Zombie.cs`.
Para mudar ou criar mapas, edite `MapDefs.cs`: cada mapa é um desenho de 27 linhas x 48 colunas
(`#` terra, `S` pedra, `=` trilho, `a` ácido, `X` serra, `^` espinhos, `1-8` largada, `G` cérebro...),
mais a lista de trens e serras móveis.
Para mudar a pontuação, veja `WinScore` e `Points*` no topo de `GameManager.cs`.

### Modo de teste automático

O executável aceita `-zumbiDemo`: bots (a mesma IA do jogo) entram no lobby, jogam sozinhos e o jogo salva prints de cada fase.

```
./ZumbiHorse.x86_64 -screen-fullscreen 0 -zumbiDemo -zumbiBots 4 -zumbiRounds 2 -zumbiShots /tmp/prints
```

Outras opções: `-zumbiNoItems` (bots colocam itens fora do caminho), `-zumbiClumsy` (último bot nunca pula),
`-zumbiWall` (paredão de pedra na frente da chegada: fase impossível), `-zumbiPauseShot` (print da tela de pausa),
`-zumbiMute` (sem som), `-zumbiHostIp` (abre uma sala na rede local) e `-zumbiJoinIp 127.0.0.1` (entra nela com um
jogador de teste que aperta os botões sozinho). Para testar o online sem abrir janelas, rode os dois com
`-batchmode -nographics`; o log mostra o tráfego e quantos sprites o cliente está copiando.
`-zumbiWin N` (pontos para vencer), `-zumbiMap N` (0 a 4 = mapa, 5 = aleatório),
`-zumbiBox Portal,Esteira,...` (força os itens da caixa), `-zumbiDiff 0|1|2|mix` (dificuldade dos bots).
