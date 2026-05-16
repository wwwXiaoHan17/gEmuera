# Dual Core Mobile Runtime Plan

This project must support two real execution cores:

- `Emuera1824V18`: the current legacy core, kept for classic v18 games.
- `SnakeModernMobile`: a Godot/mobile-adapted port of `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp`.

The existing `Program.IsSnakeProfile` compatibility branch is not a real modern core. It contains fallback methods that return default values for missing modern APIs. Those fallbacks prevent missing-function errors, but they do not reproduce the reference runtime behavior.

The current code now separates these concepts:

- `EmueraCoreProfile.Snake`: legacy v18 core plus temporary Snake compatibility behavior.
- `EmueraCoreProfile.SnakeModernMobile`: reserved for the real Godot/mobile modern core.
- `Program.UseLegacySnakeCompatibilityFallbacks`: the only place where fallback methods may be registered.

## Non-Negotiable Rules

- Do not reference the reference project's `Emuera.csproj` directly. It targets `net8.0-windows` and depends on Windows Forms, SkiaSharp WindowsForms, NAudio, and desktop-only APIs.
- Do not treat `SnakeIntFallbackMethod` or `SnakeStringFallbackMethod` as compatibility. They are temporary stubs and must not be used for the modern mobile core.
- Keep the current Godot shell: launcher, Android game directory scanning, `user://` storage, Godot UI, input bridge, image/sound adaptation.
- Port only the script/runtime logic needed for APK execution, then replace desktop dependencies with existing Godot/uEmuera adapters.

## Target Boundaries

### Legacy Core

The current `MinorShift.Emuera.GameProc` / `MinorShift.Emuera.GameData` runtime remains the v18 core. Its behavior should stay stable for older games.

### Modern Mobile Core

The modern core is based on the reference project's `Emuera/Runtime/Script` model:

- `EraType`
- `AExpression`
- `ExecutionContext`
- `VariableDescriptor`
- `SparseArray<T>` / `SparseArray2D<T>`
- modern `VariableData`, `VariableToken`, `VariableEvaluator`
- modern `ErbLoader`, `LabelDictionary`, `ParserMediator`
- modern `FunctionMethod` and real implementations for Float, VARIADIC, XML, MAP, DT, SQL, enum/reflection APIs

Desktop-only dependencies in that code must be adapted:

- `System.Windows.Forms` clipboard/dialog access -> Godot/uEmuera bridge
- `System.Drawing` colors/fonts/images -> existing `uEmuera.Drawing` and Godot rendering bridge
- `MinorShift.Emuera.UI.Game.*` -> current `MinorShift.Emuera.GameView` / `EmueraContent`
- Windows/NAudio sound APIs -> current mobile-safe audio behavior
- file writes -> Android-safe game directory or `user://`

## Migration Order

1. Freeze the legacy v18 core surface.
2. Create a separate modern-mobile namespace or folder so modern code does not collide with current v18 classes.
3. Move pure model/runtime files first: `EraType`, `SparseArray`, `ExecutionContext`, expression classes, parser words.
4. Move variable system: descriptors, codes, tokens, evaluator, user-defined variables, save/load support.
5. Move function system with real methods. Remove fallback registration for the modern core.
6. Move loader/process/call stack and reconnect LazyLoading.
7. Add a small core adapter selected by `Program.CoreProfile`, so Godot UI calls either v18 or modern mobile runtime through the same input/output bridge.
8. Build Android and test with the target game before adding optional desktop-only APIs.

## Completion Criteria

- `TOFLOAT`, `TOSTRF`, `#DIMF`, `LOCALF`, `ARGF`, `RESULTF`, `#FUNCTIONF` work as real Float features.
- `VARIADIC ARG/ARGS/ARGF` works with modern call binding.
- Sparse arrays prevent large-index OOM on Android.
- XML/MAP/DT/SQL functions either have real mobile implementations or explicit, documented unsupported errors. They must not silently return `0` or `""`.
- LazyLoading uses the mobile-safe index path and works in both cores.
- The APK runs the target Snake-profile game without relying on desktop-only APIs.

## Temporary Core Selection

Until the modern mobile runtime is fully wired, normal `snake` folders continue to use `EmueraCoreProfile.Snake` for the legacy compatibility path.

`EmueraCoreProfile.SnakeModernMobile` is selected only when the game folder or a parent folder is named `snake-modern` / `snake_modern`, or when the game directory contains `modern_core.txt` / `snake_modern_core.txt`.

## Current Port Status

- Added `Scripts/Emuera/Modern/Script` as the isolated modern-mobile core namespace.
- Added modern core primitives: `EraType`, `SparseArray<T>`, `SparseArray2D<T>`, `ExecutionContext`.
- Added expression primitives: `AExpression`, `SingleLongTerm`, `SingleStrTerm`, `SingleFloatTerm`.
- Added modern variable metadata foundation: `VariableCode`, `VariableDescriptor`, `VariableDescriptorTable`.
- Added sparse/float variable storage skeleton: `ModernVariableData` with `RESULTF`, `LOCALF`, and `ARGF` channels.
- Added modern variable access layer: `ModernVariableToken`, `ModernVariableTerm`, and `ModernVariableEvaluator`.
- The isolated modern layer can now resolve built-in int/string/float variables, read/write sparse 1D arrays, read/write `RESULTF`, and use `ExecutionContext` backed `LOCAL/ARG/LOCALS/ARGS/LOCALF/ARGF`.
- Added first modern parser entry points: `ModernExpressionParser` and `ModernVariableParser`.
- The parser can now turn ERB-style variable expressions such as `LOCALF:0`, `RESULTF`, and `FLAG:999999` into `ModernVariableTerm` instances. This first parser pass intentionally covers variables and literals only; function calls, operators, keyword index conversion, and full ERB line parsing still need to be ported from the reference parser.
- Added modern assignment/line handling: `ModernStatement`, `ModernAssignmentStatement`, and `ModernLineParser`.
- The modern line parser now routes `RESULTF = 1.5` and `LOCALF:0 = RESULTF` through `ModernVariableTerm.SetValue()`.
- Added basic modern expression operators: unary `+/-`, parentheses, `*`, `/`, `%`, `+`, and `-`.
- Assignment RHS expressions such as `RESULTF + 1.5`, `LOCAL:0 + 1`, and parenthesized numeric expressions now flow through the modern expression tree. Function calls, bit operators, ternary expressions, and complete tokenized ERB parsing are still pending.
- Added comparison/logical expression support matching the reference core's integer truth-value style: `==`, `!=`, `>`, `<`, `>=`, `<=`, `&&`, `||`, and unary `!`.
- Modern assignment parsing now distinguishes plain assignment `=` from comparison operators such as `==`, `!=`, `<=`, and `>=`.
- Added first modern function-call layer: `ModernFunctionEvaluator`, `ModernFunctionMethod`, and `ModernFunctionCallExpression`.
- The modern expression parser can now parse pure logic function calls such as `TOFLOAT(...)`, `TOSTRF(...)`, `SQRT(...)`, `CBRT(...)`, `LOG(...)`, `LOG10(...)`, `EXPONENT(...)`, `SIGN(...)`, trigonometric functions, and `FLOOR/CEIL/ROUND`. Desktop-bound functions remain intentionally outside this layer until Godot/mobile adapters exist.
- Added first modern multi-line statement layer: `ModernBlockStatement`, `ModernIfStatement`, and `ModernScriptParser`.
- The modern script parser can execute assignment blocks with nested `IF / ELSEIF / ELSE / ENDIF`, using the same integer truth-value semantics as the reference core.
- Added `WHILE / WEND` block execution through `ModernWhileStatement`, sharing the same condition semantics as `IF`.
- Added `FOR / NEXT` block execution through `ModernForStatement`.
- `FOR` now accepts integer counters with `FOR var, start, end[, step]`, supports an omitted start as `0`, uses the reference core's exclusive end comparison, and applies unchecked counter stepping to match the modern runtime loop behavior.
- Added `REPEAT / REND` parsing on top of the same loop statement model. It uses `COUNT:0` as the loop counter, starts at `0`, and stops before the requested repeat count, matching the reference runtime's internal `SpForNextArgment` behavior.
- Added modern loop-control statements: `BREAK` and `CONTINUE`.
- `WHILE` handles `BREAK/CONTINUE` by exiting or rechecking the condition. `FOR/REPEAT` handles them with the reference-compatible counter behavior: both `BREAK` and `CONTINUE` advance the loop counter once before leaving or rechecking the loop.
- Added `SIF` parsing and execution for single-line conditional statements. The current modern scaffold supports assignment, `BREAK`, and `CONTINUE` as the following statement, and rejects nested block-control targets until the full instruction-line model is ported.
- Added `DO / LOOP` post-condition loop execution. The body runs once before evaluating the `LOOP` condition, `CONTINUE` jumps to that condition check, and `BREAK` exits the loop.
- Added `SELECTCASE / CASE / CASEELSE / ENDSELECT` parsing and execution. The modern scaffold now supports first-match branch dispatch, multiple comma-separated case expressions, `CASE value`, `CASE left TO right`, and `CASE IS <operator> value` for integer, float, and string selectors.
- Added `RETURN` and `RETURNF` execution in the modern statement layer. `RETURN` writes integer return values into `RESULT:0..n`, while `RETURNF` returns a typed `SingleTerm` and updates `RESULT`, `RESULTS`, or `RESULTF` according to the returned value type.
- Added `ModernScriptFunctionMethod`, allowing pure modern script blocks to be registered in `ModernFunctionEvaluator` as callable methods. Calls create a child `ExecutionContext`, bind positional arguments into `ARG/ARGS/ARGF`, execute the block, and coerce the returned value to the declared `EraType`.
- Added `ModernScriptModuleParser` and `ModernScriptModule` for the first isolated ERB module pass.
- The modern module parser now discovers `@LABEL` sections, recognizes `#FUNCTION`, `#FUNCTIONS`, and `#FUNCTIONF`, pre-registers script functions before parsing bodies for forward calls, and then parses each function body into the modern statement tree.
- Added first function-local sizing support for the modern module pass.
- `ModernScriptFunctionMethod` now carries per-function `LOCAL`, `LOCALS`, `LOCALF`, `ARG`, `ARGS`, and `ARGF` lengths and uses them when creating child `ExecutionContext` instances.
- `ModernScriptModuleParser` now reads `#LOCALSIZE`, `#LOCALSSIZE`, `#LOCALFSIZE`, `#ARGSIZE`, `#ARGSSIZE`, and `#ARGFSIZE`. It also recognizes local-size forms of `#DIM LOCAL`, `#DIMS LOCALS`, `#DIMF LOCALF`, `#DIM ARG`, `#DIMS ARGS`, and `#DIMF ARGF`. Full private-variable `#DIM/#DIMS/#DIMF` namespacing is still pending.
- Added `ModernUserVariableDefinition` and first private `#DIM/#DIMS/#DIMF` declaration collection.
- Function labels now expose private variable metadata including name, integer/string/float kind, dimensions, lengths, constant default values, and common attributes such as `STATIC`, `DYNAMIC`, `CONST`, `REF`, and `OUT`. Function-scoped variable lookup/storage is the next step before these declarations become executable variables.
- Added function-scoped private variable runtime binding for the isolated modern core.
- `ModernPrivateVariableStore` now backs private `#DIM/#DIMS/#DIMF` values with sparse integer/string/float slots and supports per-call dynamic stores over persistent static stores.
- `ModernPrivateVariableToken` exposes those private slots through the same `ModernVariableTerm` path as built-in variables, so function bodies can parse and execute assignments such as `#DIMF TEMP, 4` followed by `TEMP:0 = RESULTF`.
- `ModernScriptModuleParser` now builds a scoped `ModernVariableEvaluator` per script function, registers that function's private variables before parsing its body, and passes the matching private definitions into `ModernScriptFunctionMethod`.
- `ModernScriptFunctionMethod` now attaches the private variable store to each child `ModernExpressionContext`, preserving default static private-variable behavior while recreating `DYNAMIC` private storage per call. `REF`/`OUT` declarations are still parsed and isolated from static storage, but true caller-reference binding remains pending.
- Added first modern function argument binding support for label signatures such as `@FUNC(ARG:0, ARGS:0 = "x", VARIADIC ARGF:0)`.
- `ModernFunctionArgumentBinding` records the destination variable, optional constant default value, and `VARIADIC` marker for each script-function parameter.
- Script function calls now bind declared parameters into the callee context instead of only copying positional values into `ARG/ARGS/ARGF` by type. Missing declared values use their constant default when present, otherwise the type zero value.
- `VARIADIC` is validated as the final parameter and limited to `ARG`, `ARGS`, or `ARGF`, matching the reference runtime's label parser rule. Extra caller arguments are packed into the selected argument array starting at the declared index, and `ExecutionContext.CurrentVariadicArgCount` records the packed count for the later `GETARGCOUNT`/runtime bridge work.
- Added the modern `ARGLEN()` function, matching the reference core's `ArgLengthMethod`, and a temporary `GETARGCOUNT()` alias for migration compatibility. Both return the current frame's packed `VARIADIC` argument count.
- Added first `REF/OUT` runtime binding for function-scoped private variables. A `ModernVariableReference` captures the caller-side `ModernVariableTerm` plus evaluated indices, and a private reference slot delegates reads/writes back to that caller variable.
- `ModernScriptFunctionMethod` now detects reference destinations during argument binding and requires the caller argument to be a variable expression with a matching `EraType`. Full reference-array matching and the reference core's complete `ElementRefInfo` compatibility checks are still pending.
- Added the first modern reflection/helper function batch: `EXISTVAR`, `EXISTFUNCTION`, `ENUMFUNCBEGINSWITH`, `ENUMFUNCENDSWITH`, `ENUMFUNCWITH`, `ENUMVARBEGINSWITH`, `ENUMVARENDSWITH`, `ENUMVARWITH`, `ENUMFILES`, and `EXISTFILE`.
- `EXISTVAR` now reports the reference-compatible type bitmask for modern variables: integer `1`, string `2`, const `4`, 2D `8`, 3D `16`, float `32`. Its expression-check mode parses through the modern expression parser.
- `EXISTFUNCTION` now reflects the modern function registry and returns `2` for integer functions, `3` for string functions, `4` for float functions, and `1` for other callable labels.
- `ENUMFUNC*`, `ENUMVAR*`, and `ENUMMACRO*` enumerate the modern function/variable/macro registries and write matches into either the supplied string variable array or `RESULTS`. Macro enumeration reads the legacy-loaded identifier dictionary while the modern macro table is still being split out.
- Added dynamic variable access helpers `GETVAR`, `GETVARF`, `GETVARS`, and `SETVAR`. They parse the variable expression string through the modern expression parser, so paths such as `LOCALF:0`, `RESULTF`, and private variables use the same `ModernVariableTerm` read/write behavior as normal script code.
- Added bit-array helpers `BITSET`, `BITGET`, and `BITTOGGLE` for integer 1D variables. The implementation follows the reference core's 64-bit slot model while operating directly on the modern token storage, avoiding dense expansion of sparse arrays.
- Added unchecked integer arithmetic helpers `UNCHECKED_ADD`, `UNCHECKED_SUB`, `UNCHECKED_MUL`, and `UNCHECKED_NEG`, matching the reference core's explicit wraparound arithmetic behavior.
- Added `REGEXPMATCH` with the reference core's two output forms: `REGEXPMATCH(text, pattern, flag)` writes group count to `RESULT:1` and captures to `RESULTS`, while `REGEXPMATCH(text, pattern, refCount, refStrings)` writes into caller-supplied variables.
- Added pure runtime memory helpers `GETMEMORYUSAGE` and `CLEARMEMORY`. These use `Process.GetCurrentProcess().WorkingSet64` plus `GC.Collect()` and do not depend on WinForms, SkiaSharp, NAudio, or other desktop UI APIs.
- Added the modern `XML_*` document subsystem backed by in-core `XmlDocument` storage. `XML_DOCUMENT`, `XML_EXIST`, `XML_RELEASE`, `XML_TOSTR`, `XML_GET`, `XML_GET_BYNAME`, `XML_SET`, `XML_SET_BYNAME`, `XML_ADDNODE`, `XML_ADDNODE_BYNAME`, `XML_ADDATTRIBUTE`, `XML_ADDATTRIBUTE_BYNAME`, `XML_REMOVENODE`, `XML_REMOVENODE_BYNAME`, `XML_REMOVEATTRIBUTE`, `XML_REMOVEATTRIBUTE_BYNAME`, `XML_REPLACE`, and `XML_REPLACE_BYNAME` now use the modern function/variable path without desktop dependencies.
- Added the first real `MAP_*` data subsystem to the modern variable data layer. `MAP_CREATE`, `MAP_EXIST`, `MAP_RELEASE`, `MAP_GET`, `MAP_SET`, `MAP_HAS`, `MAP_REMOVE`, `MAP_CLEAR`, `MAP_SIZE`, `MAP_GETKEYS`, `MAP_VALUES`, `MAP_TOXML`, `MAP_FROMXML`, `MAP_MERGE`, `MAP_REMOVEIF`, `MAP_FINDKEY`, `MAP_TOSTRING`, and `MAP_FROMSTRING` now operate on in-core string maps without desktop dependencies.
- Added the first real `DT_*` data-table subsystem backed by `System.Data.DataTable` in the modern variable data layer. `DT_CREATE`, `DT_EXIST`, `DT_RELEASE`, `DT_NOCASE`, `DT_CLEAR`, `DT_COLUMN_ADD`, `DT_COLUMN_EXIST`, `DT_COLUMN_REMOVE`, `DT_COLUMN_NAMES`, `DT_COLUMN_LENGTH`, `DT_ROW_ADD`, `DT_ROW_SET`, `DT_ROW_REMOVE`, `DT_ROW_LENGTH`, `DT_CELL_GET`, `DT_CELL_GETS`, `DT_CELL_GETF`, `DT_CELL_ISNULL`, `DT_CELL_SET`, `DT_CELL_SETF`, `DT_SELECT`, `DT_TOXML`, and `DT_FROMXML` now have non-stub implementations with typed columns, row IDs, result-array output, and XML schema/data round-tripping.
- Added the first modern `SQL_*` subsystem backed by `Microsoft.Data.Sqlite`, which is already referenced by the Godot project. `SQL_CONNECTION_OPEN`, `SQL_CONNECT`, `SQL_DISCONNECT`, `SQL_EXECUTE_NONQUERY`, `SQL_EXECUTE_READER`, `SQL_READER_READ`, `SQL_READER_GET_LONG`, `SQL_READER_GET_FLOAT`, `SQL_READER_GET_STRING`, `SQL_READER_ISNULL`, `SQL_READER_CLOSE`, scalar query helpers, parameterized `SQL_P_*` helpers, `SQL_ESCAPE`, and MAP/DT/XML import-export helpers now have real modern implementations.
- `ModernSqlManager` keeps SQL isolated from legacy `SnakeSqlManager` and desktop runtime paths. When `SnakeModernMobile` is selected, `Program` now maps modern SQL storage to Godot's Android-safe user data directory.
- Added modern `OUTPUTLOG` through the current Godot console log exporter, including the reference-compatible optional filename and hide-info arguments.
- Added modern `GETDOINGFUNCTION` by tracking the current modern script function name in `ExecutionContext`.
- Added modern dynamic method helpers `GETMETH`, `GETMETHF`, `GETMETHS`, and `EXISTMETH`, so scripts can call registered modern functions by name with fallback values.
- Added another pure/mobile-safe modern function batch: `TOSTR`, `TOINT`, `TOUPPER`, `TOLOWER`, `TOHALF`, `TOFULL`, `STRLENS`, `STRLENSU`, `SUBSTRING`, `SUBSTRINGU`, `STRFIND`, `STRFINDU`, `STRCOUNT`, `STRJOIN`, `REPLACE`, `UNICODE`, `UNICODEBYTE`, `ENCODETOUNI`, `CHARATU`, `ISNUMERIC`, `ESCAPE`, and `CONVERT`.
- Added dynamic/text helper functions `EVAL`, `EVALF`, `EVALS`, `BARSTR`, `MONEYSTR`, `LINEISEMPTY`, `PRINTCPERLINE`, `PRINTCLENGTH`, `SAVENOS`, `GETTIME`, `GETTIMES`, `GETSECOND`, `GETMILLISECOND`, `GETCONFIG`, `GETCONFIGS`, `COLOR_FROMRGB`, and `COLOR_FROMNAME`.
- Added pure HTML text helpers `HTML_ESCAPE`, `HTML_TOPLAINTEXT`, `HTML_STRINGLEN`, `HTML_SUBSTRING`, and `HTML_STRINGLINES`. The current Godot shell lacks the reference core's display-node `HtmlLength/HtmlSubString`, so the modern implementations use mobile-safe plain-text width fallback until the full HTML display bridge is ported.
- Added array/search helpers `RAND`, `MAX`, `MIN`, `LIMIT`, `INRANGE`, `SUMARRAY`, `MATCH`, `INRANGEARRAY`, `MAXARRAY`, `MINARRAY`, `FINDELEMENT`, `FINDLASTELEMENT`, `MATCHALL`, `MATCHALLEX`, `GROUPMATCH`, `NOSAMES`, and `ALLSAMES`.
- Corrected `INRANGEARRAY` to use the reference-compatible half-open range rule: values count when `min <= value < max`.
- Added variable/bit/level helpers `VARSIZE`, `VARSETEX`, `GETBIT`, `BITINDEXOFFIRST`, `GETNUM`, `GETNUMB`, `GETPALAMLV`, `GETEXPLV`, and `ISDEFINED`. Modern `PALAMLV` and `EXPLV` arrays now receive the configured default level thresholds during `ModernVariableData` initialization, and `GETNUM/GETNUMB` bridge to the loaded constant data when available.
- Added `ARRAYMSORT` and `ARRAYMSORTEX` for the modern mobile core's supported one-dimensional integer/string/float arrays. `ARRAYMSORT` follows the reference behavior of sorting until the first default key value; `ARRAYMSORTEX` adds descending order and fixed-length support.
- Added Godot-console-backed state helpers: `CLIENTWIDTH`, `CLIENTHEIGHT`, `GETCOLOR`, `GETDEFCOLOR`, `GETFOCUSCOLOR`, `GETBGCOLOR`, `GETDEFBGCOLOR`, `GETSTYLE`, `GETFONT`, `CURRENTALIGN`, and `CURRENTREDRAW`. These read the active `GlobalStatic.Console`/`Config` state and throw if no console is attached instead of returning stub values.
- Added legacy-loaded CSV/template bridge functions for the modern core: `CSVNAME`, `CSVCALLNAME`, `CSVNICKNAME`, `CSVMASTERNAME`, `CSVCSTR`, `CSVBASE`, `CSVABL`, `CSVMARK`, `CSVEXP`, `CSVRELATION`, `CSVTALENT`, `CSVCFLAG`, `CSVEQUIP`, `CSVJUEL`, `EXISTCSV`, and `GETCSVNOBY*`. These read real `ConstantData`/`VariableEvaluator` character templates and preserve SP-character option checks.
- Added legacy-backed DAT query helpers `CHKDATA`, `CHKGLOBALDATA`, `CHKVARDATA`, `CHKCHARADATA`, `FIND_VARDATA`, and `FIND_CHARADATA`, with result text/file matches written through the modern `RESULTS` path.
- Added Godot-console-backed input/display helpers: `GETLINESTR`, `ISSKIP`, `MESSKIP`, `MOUSESKIP`, `ISACTIVE`, `GETKEY`, `GETKEYTRIGGERED`, `MOUSEX`, `MOUSEY`, `HTML_GETPRINTEDSTR`, `HTML_POPPRINTINGSTR`, and `GETDISPLAYLINE`.
- Added Snake display-quality readers/writers `GET_TEXT_DRAWING_MODE`, `SET_TEXT_DRAWING_MODE`, `GET_SKIA_QUALITY`, and `SET_SKIA_QUALITY`, backed by the current Godot console's Snake rendering state.
- Added modern mobile-backed sound/textbox/text-file helpers: `EXISTSOUND`, `ISPLAYINGSOUND`, `ISPLAYINGBGM`, `SOUNDCONTROL`, `BGMCONTROL`, `GETSOUNDORBGMINFO`, `GETANIMETIMER`, `CHKFONT`, `SAVETEXT`, `LOADTEXT`, `EXISTSIMAGELAYER`, `GETTEXTBOX`, `SETTEXTBOX`, and `ERDNAME`.
- Added legacy-character-data-backed modern variable tokens for `ISASSI`, `NO`, `NAME`, `CALLNAME`, `NICKNAME`, `MASTERNAME`, core 1D character arrays, `CSTR`, and `CDFLAG`. The modern variable parser now accepts reference-compatible character variable forms such as `NAME:chara`, `BASE:chara:index`, and omitted-target forms backed by the current legacy `TARGET`.
- Added modern character runtime helpers `GETCHARA`, `GETSPCHARA`, `FINDCHARA`, `FINDLASTCHARA`, `CMATCH`, `SUMCARRAY`, `MAXCARRAY`, `MINCARRAY`, and `INRANGECARRAY`, using real legacy-loaded character state instead of fallback stubs.
- Added `STRFORM`, backed by the legacy formatted-string analyzer and modern expression context.
- Added `FLOWINPUT` and `FLOWINPUTS`, wiring default int/string input values and skip behavior into the current `Process` wait-input path.
- Added modern `HOTKEY_STATE_INIT` and `HOTKEY_STATE`, preserving the reference state-array API without depending on the reference WinForms window.
- Added the first real Godot image-buffer bridge for modern graphics/sprite/CBG functions, including `GCREATE`, `GCREATEFROMFILE`, `GLOAD`, `GSAVE`, `GCLEAR`, `GFILLRECTANGLE`, `GDRAWG`, `GDRAWGWITHMASK`, `GDRAWSPRITE`, `GSETCOLOR`, `GSETBRUSH`, sprite create/state/position/dispose/anime helpers, and CBG set/clear/button-map helpers.
- Function registry comparison against the Snake reference currently reports all `348 / 348` reference functions registered in the modern mobile function registry. The scanner sees one additional internal registration shape in the modern file, but the reference-side missing set is empty.
- Added the remaining Godot/mobile-safe graphics and input bridge batch: line/polygon drawing, font/pen/dash/rotation state, `GDRAWTEXT` via Godot `SubViewport` text rasterization, `MOUSEB` hover state, and text-box positioning state helpers.
