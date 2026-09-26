#Requires -Version 7
<#
.SYNOPSIS
    Seeds Debugskolen with realistic demo data via the live API.

.DESCRIPTION
    Requires the Aspire stack to be running (aspire run --non-interactive).
    DbSeeder.cs must already have run (school, admin staff, subscription, courses).
    Script is NOT idempotent — run against a fresh database only.
    Use scripts/nuke.ps1 to reset, then restart Aspire before re-seeding.

.PARAMETER ApiBase
    Base URL of the API. Default: http://localhost:5000

.PARAMETER KeycloakBase
    Base URL of Keycloak. Default: http://localhost:8080

.PARAMETER Username
    Keycloak username. Default: admin@debugskolen.dk

.PARAMETER Password
    Keycloak password. Default: test1234
#>

param(
    [string]$ApiBase = "http://localhost:5000",
    [string]$KeycloakBase = "http://localhost:8080",
    [string]$Username = "admin@debugskolen.dk",
    [string]$Password = "test1234"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─── helpers ───────────────────────────────────────────────────────────────────

$token = $null

function Get-Token {
    $body = @{
        grant_type = "password"
        client_id  = "skoleoverblikket-web"
        username   = $Username
        password   = $Password
        scope      = "openid profile roles tenant"
    }
    $resp = Invoke-RestMethod `
        -Uri "$KeycloakBase/realms/Skoleoverblikket/protocol/openid-connect/token" `
        -Method Post `
        -Body $body `
        -ContentType "application/x-www-form-urlencoded"
    return $resp.access_token
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )
    $headers = @{ Authorization = "Bearer $token" }
    $params = @{
        Uri     = "$ApiBase$Path"
        Method  = $Method
        Headers = $headers
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
        $params.ContentType = "application/json"
    }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        $status = $_.Exception.Response?.StatusCode
        $detail = $_.ErrorDetails?.Message ?? $_.Exception.Message
        Write-Warning "  FAIL $Method $Path ($status): $detail"
        throw
    }
}

# ─── data ──────────────────────────────────────────────────────────────────────

$danishMaleFirstNames = @("Anders", "Bo", "Christian", "Daniel", "Emil", "Frederik", "Gustav", "Hans", "Ib", "Jacob", "Karl", "Lars", "Mikkel", "Niels", "Ole", "Peter", "Rasmus", "Søren", "Thomas", "Ulrik")
$danishFemaleFirstNames = @("Anna", "Birthe", "Charlotte", "Dorte", "Elisabeth", "Freja", "Gitte", "Hanne", "Ida", "Julie", "Karen", "Lene", "Marie", "Nina", "Oda", "Pia", "Rikke", "Sara", "Tine", "Ulla")
$danishLastNames = @("Hansen", "Nielsen", "Jensen", "Pedersen", "Andersen", "Christensen", "Larsen", "Sørensen", "Rasmussen", "Petersen", "Jørgensen", "Madsen", "Kristensen", "Thomsen", "Poulsen", "Mortensen", "Knudsen", "Lund", "Møller", "Berg")

$allFirstNames = $danishMaleFirstNames + $danishFemaleFirstNames
$nameIndex = 0

function Next-Name {
    param([string]$LastName = $null)
    $first = $allFirstNames[$nameIndex % $allFirstNames.Count]
    $script:nameIndex++
    $last = if ($LastName) { $LastName } else { $danishLastNames[$nameIndex % $danishLastNames.Count] }
    return "$first $last"
}

# Grade → courses (by name). Younger grades: core subjects only. Older: full palette.
$gradeCourses = @{
    0 = @("Dansk", "Matematik", "Musik", "Idræt", "Billedkunst", "Kristendomskundskab")
    1 = @("Dansk", "Matematik", "Engelsk", "Musik", "Idræt", "Billedkunst", "Kristendomskundskab")
    2 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Musik", "Idræt", "Billedkunst", "Kristendomskundskab")
    3 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Historie", "Musik", "Idræt", "Kristendomskundskab")
    4 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Historie", "Musik", "Idræt", "Kristendomskundskab", "Håndværk og design")
    5 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Historie", "Musik", "Idræt", "Kristendomskundskab", "Håndværk og design", "Billedkunst")
    6 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Historie", "Musik", "Idræt", "Kristendomskundskab", "Geografi", "Biologi", "Tysk")
    7 = @("Dansk", "Matematik", "Engelsk", "Naturfag", "Historie", "Idræt", "Kristendomskundskab", "Geografi", "Biologi", "Fysik/kemi", "Tysk", "Samfundsfag")
    8 = @("Dansk", "Matematik", "Engelsk", "Historie", "Idræt", "Geografi", "Biologi", "Fysik/kemi", "Tysk", "Fransk", "Samfundsfag")
}

$weekPlanTemplates = @{
    "Dansk"               = @(
        @{ Beskrivelse = "Vi læser kapitel 4 i 'Bambus-drengen' og taler om personernes følelser."; Lektier = "Læs side 45-52 og skriv 5 sætninger om din yndlingsperson i bogen." }
        @{ Beskrivelse = "Stavning og grammatik: kommatering og sætningsled."; Lektier = "Lav opgaverne på side 78 i grammatikbogen." }
        @{ Beskrivelse = "Skriveværksted: eleverne arbejder med deres egne fortællinger."; Lektier = "Ret din fortælling og skriv en ny slutning." }
        @{ Beskrivelse = "Vi arbejder med nutidsform og fortidsform af uregelmæssige verber."; Lektier = "Lær de 20 uregelmæssige verber på side 34." }
        @{ Beskrivelse = "Fælles højtlæsning og efterfølgende diskussion om handlingen."; Lektier = "Svar på spørgsmål 1-5 i opgavehæftet." }
    )
    "Matematik"           = @(
        @{ Beskrivelse = "Vi gennemgår multiplikation og division med store tal. Brug lommeregneren til at tjekke svar."; Lektier = "Side 62, opgave 1-10." }
        @{ Beskrivelse = "Brøker: hvad er en tæller og en nævner? Vi øver os med pizzastykker."; Lektier = "Tegn 3 forskellige brøker og skriv dem med tal." }
        @{ Beskrivelse = "Geometri: arealet af rektangler og trekanter. Praktisk øvelse med målebånd."; Lektier = "Mål dit værelse hjemme og beregn arealet." }
        @{ Beskrivelse = "Procentregning: hvad er 25% af 200? Vi løser opgaver i makkerpar."; Lektier = "Side 88, opgave 1-8." }
        @{ Beskrivelse = "Mundtlig matematik: eleverne præsenterer deres løsningsstrategier for klassen."; Lektier = "Løs de 5 udfordringsopgaver bagest i kapitlet." }
    )
    "Engelsk"             = @(
        @{ Beskrivelse = "Vi ser en kort film på engelsk og taler om handlingen. Fokus på lytteforståelse."; Lektier = "Skriv 10 nye gloser fra filmen og lær dem." }
        @{ Beskrivelse = "Rollespil: at bestille mad på en engelsk restaurant. Øv med din makker."; Lektier = "Øv dialogen med en hjemme — du må gerne filme det." }
        @{ Beskrivelse = "Grammatik: simple past vs. present perfect. Gennemgang og øvelser."; Lektier = "Udfyld opgaverne side 54 i workbook." }
        @{ Beskrivelse = "Vi læser en artikel om klimaforandringer og diskuterer på engelsk."; Lektier = "Skriv et kort svar (5-8 sætninger) på spørgsmålet i slutningen af artiklen." }
        @{ Beskrivelse = "Præsentationer: eleverne fortæller om et dansk kulturelt fænomen på engelsk."; Lektier = "Forbered 2-minutters præsentation til næste gang." }
    )
    "Naturfag"            = @(
        @{ Beskrivelse = "Eksperiment: vi undersøger hvad planter har brug for for at gro."; Lektier = "Tegn og beskriv plantens livscyklus." }
        @{ Beskrivelse = "Hvad er fotosyntese? Vi kigger på klorofyl under mikroskop."; Lektier = "Lær definitionen på fotosyntese udenad." }
        @{ Beskrivelse = "Vejret og klimaet: målinger og grafer. Vi bruger skolens vejrstation."; Lektier = "Mål temperaturen ude hjemme tre gange i morgen og noter det." }
        @{ Beskrivelse = "Kroppen: fordøjelsessystemet. Video og tegning af organerne."; Lektier = "Navngiv de 6 organer vi gennemgik i dag." }
        @{ Beskrivelse = "Energikilder: sol, vind og fossile brændsler. Fordele og ulemper."; Lektier = "Find ét eksempel hjemmefra på vedvarende energi og beskriv det." }
    )
    "Historie"            = @(
        @{ Beskrivelse = "Vi begynder på forløbet om Vikingetiden. Hvem var vikingerne?"; Lektier = "Læs side 12-18 i historiebogen og svar på spørgsmål 1-3." }
        @{ Beskrivelse = "Gennemgang af Anden Verdenskrig: årsager og forløb."; Lektier = "Se den korte dokumentar på linket og skriv 5 facts ned." }
        @{ Beskrivelse = "Kildekritik: hvad er en primærkilde? Vi analyserer tre historiske dokumenter."; Lektier = "Find en historisk kilde selv og skriv en kort analyse." }
        @{ Beskrivelse = "Danmarkshistorie: grundlovens tilblivelse i 1849."; Lektier = "Læs grundlovens §1 og forklar den med dine egne ord." }
        @{ Beskrivelse = "Rollespil: vi simulerer et møde i FN om klimakonflikt."; Lektier = "Forbered argumenter for dit lands holdning." }
    )
    "Musik"               = @(
        @{ Beskrivelse = "Vi øver skolesangen til årets skolefest. Fokus på intonation og rytme."; Lektier = "Lyt til sangen én gang og øv teksten." }
        @{ Beskrivelse = "Introduktion til noder: takt, rytme og melodi. Vi spiller på xylofoner."; Lektier = "Øv de to takter vi lavede i dag." }
        @{ Beskrivelse = "Vi lytter til klassisk musik og taler om hvad vi føler. Beethoven vs. Mozart."; Lektier = "Skriv 3 ord der beskriver den musik du bedst kan lide." }
        @{ Beskrivelse = "Fællessang og sang med kanon. Vi arbejder med stemmeleje."; Lektier = "Øv din stemme hjemme — syng en sang du kender." }
        @{ Beskrivelse = "Rytmik: vi bruger kroppen som instrument. Trampe- og klappeøvelser."; Lektier = "Ingen lektier i dag — god fornøjelse!" }
    )
    "Idræt"               = @(
        @{ Beskrivelse = "Boldspil: mini-fodbold i små hold. Fokus på samarbejde og fair play."; Lektier = "Ingen lektier." }
        @{ Beskrivelse = "Atletik: vi måler løbetider og øver spring. Forberedelse til idrætsprøven."; Lektier = "Stræk ud og løb 10 minutter i løbet af weekenden." }
        @{ Beskrivelse = "Dans og bevægelse: street dance intro. Lær de grundlæggende trin."; Lektier = "Øv de 4 trin vi lærte i dag." }
        @{ Beskrivelse = "Svømning: crawl og rygcrawl. Fokus på teknik."; Lektier = "Ingen lektier." }
        @{ Beskrivelse = "Udespil: orienteringsløb i skolens omgivelser."; Lektier = "Ingen lektier." }
    )
    "Kristendomskundskab" = @(
        @{ Beskrivelse = "Vi taler om de store verdensreligioner: hvad har de til fælles?"; Lektier = "Læs side 22-26 og svar på de to spørgsmål." }
        @{ Beskrivelse = "Biblen: Adam og Eva og skabelsesberetningen. Hvad fortæller den os?"; Lektier = "Skriv din egen 'skabelsesberetning' i 10 sætninger." }
        @{ Beskrivelse = "Etik og moral: hvad er rigtigt og forkert? Dilemma-diskussioner."; Lektier = "Tænk på et dilemma du har oplevet og beskriv det." }
        @{ Beskrivelse = "Islam: fem søjler og Ramadan. Gæstelærer fra det lokale islamiske center."; Lektier = "Skriv 3 ting du lærte i dag." }
        @{ Beskrivelse = "Højtider: jul, påske og pinse. Hvad fejrer vi og hvorfor?"; Lektier = "Spørg en voksen derhjemme om en højtidstradition og fortæl om den næste gang." }
    )
    "Billedkunst"         = @(
        @{ Beskrivelse = "Farvelære: primær- og sekundærfarver. Vi blander akrylmaling."; Lektier = "Find 3 eksempler på komplementærfarver i dit hjem." }
        @{ Beskrivelse = "Portræt: vi tegner hinanden. Fokus på proportioner og skygge."; Lektier = "Tegn et selvportræt hjemme." }
        @{ Beskrivelse = "Installationskunst: hvad er det? Besøg på skolens lille udstilling."; Lektier = "Tag et billede af noget du synes er smukt og forklar hvorfor." }
        @{ Beskrivelse = "Keramik: vi former ansigtsmasker i ler."; Lektier = "Ingen lektier." }
        @{ Beskrivelse = "Grafisk design: logo-design med digitale værktøjer."; Lektier = "Skitser et logo til en fiktiv virksomhed." }
    )
    "Håndværk og design"  = @(
        @{ Beskrivelse = "Vi bygger et fuglehus. Intro til hammer, sav og søm."; Lektier = "Mål den vinge vi skar til i dag og skriv målet ned." }
        @{ Beskrivelse = "Tekstil: vi syr en enkel pung med knaplukning."; Lektier = "Syning: øv en sting-type hjemme på et stykke stof." }
        @{ Beskrivelse = "Design-processen: skitsér, prøv, evaluer. Vi arbejder med prototyper."; Lektier = "Tegn to alternative designs til dit projekt." }
        @{ Beskrivelse = "Vi afslutter fuglehusprojektet og lakerer."; Lektier = "Ingen lektier." }
        @{ Beskrivelse = "Evaluering og udstilling: præsenter dit produkt for klassen."; Lektier = "Ingen lektier." }
    )
    "Tysk"                = @(
        @{ Beskrivelse = "Gloser og udtale: tal 1-100 på tysk. Vi synger Zahlenlied."; Lektier = "Lær gloserne på side 8 udenad." }
        @{ Beskrivelse = "Grammatik: der, die, das — artikler på tysk. Huskeregel og øvelser."; Lektier = "Skriv 10 substantiver med korrekt artikel." }
        @{ Beskrivelse = "Dialog: vi øver os i at præsentere os selv på tysk."; Lektier = "Øv din præsentation: navn, alder, by, hobbyer." }
        @{ Beskrivelse = "Tekst: vi læser en kort novelle og oversætter afsnit i grupper."; Lektier = "Oversæt afsnit 3 på side 42 til dansk." }
        @{ Beskrivelse = "Landekendskab: hvad ved vi om Tyskland? Kultur og geografi."; Lektier = "Find en tysk sportsklub og skriv 5 facts om den." }
    )
    "Fransk"              = @(
        @{ Beskrivelse = "Bonjour! Introduktion til fransk: udtale og basale hilsner."; Lektier = "Øv de 10 hilsner vi lærte i dag." }
        @{ Beskrivelse = "Farver og tøj på fransk. Vi spiller ordspillet 'Touche'."; Lektier = "Skriv 5 sætninger om dit tøj på fransk." }
        @{ Beskrivelse = "Grammatik: être og avoir i nutid. Konjugationstabel."; Lektier = "Lær konjugationerne udenad — de bruges i al fransk." }
        @{ Beskrivelse = "Paris: seværdigheder og kultur. Vi ser en kort film."; Lektier = "Skriv 3 ting du gerne vil se i Paris." }
        @{ Beskrivelse = "Rollespil: en dag i Paris — på café og i metroen."; Lektier = "Lær de 8 gloser til næste lektion." }
    )
    "Geografi"            = @(
        @{ Beskrivelse = "Kortlæsning: vi arbejder med topografiske kort og højdekurver."; Lektier = "Find dit hjemsted på et kort og beskriv terrænet." }
        @{ Beskrivelse = "Klimazoner: hvorfor er Sahara ørkenen og Amazonas regnskov?"; Lektier = "Tegn en klimazone og beskriv den." }
        @{ Beskrivelse = "Befolkningsvækst og ressourcer: FN's bæredygtighedsmål."; Lektier = "Læs om SDG nr. 2 og skriv hvad du mener om det." }
        @{ Beskrivelse = "Vi undersøger råstofudvinding i Grønland og dets konsekvenser."; Lektier = "Find en aktuel nyhedsartikel om Grønland." }
        @{ Beskrivelse = "Byplanlægning: hvad gør en by bæredygtig? Eksempler fra verden."; Lektier = "Skriv 3 idéer til at gøre din by mere grøn." }
    )
    "Biologi"             = @(
        @{ Beskrivelse = "Cellen: opbygning og funktion. Vi tegner en celle med alle organeller."; Lektier = "Lær de 8 organeller og deres funktion." }
        @{ Beskrivelse = "Fotosyntese og respiration: to sider af samme mønt."; Lektier = "Skriv reaktionsligningen for fotosyntese." }
        @{ Beskrivelse = "Genetik: hvad er DNA? Dobbelthelixens struktur."; Lektier = "Se animationsfilmen om DNA og skriv 5 facts." }
        @{ Beskrivelse = "Økosystemer: fødekæder og -net i en dansk skov."; Lektier = "Tegn en fødekæde med mindst 5 led." }
        @{ Beskrivelse = "Dissektionsøvelse: vi undersøger en fisks indre organer."; Lektier = "Skriv en rapport om hvad du observerede." }
    )
    "Fysik/kemi"          = @(
        @{ Beskrivelse = "Grundstoffer og det periodiske system. Hvad er et atom?"; Lektier = "Lær de 10 første grundstoffer udenad." }
        @{ Beskrivelse = "Elektricitet: serie- og parallelkobling. Eksperiment med batteri og lyspærer."; Lektier = "Tegn et kredsløbsdiagram fra hukommelsen." }
        @{ Beskrivelse = "Syrer og baser: pH-skalaen. Vi tester hverdagsvæsker med lakmuspapir."; Lektier = "Skriv pH-værdien for 5 væsker hjemme." }
        @{ Beskrivelse = "Kræfter og bevægelse: Newtons love i hverdagen."; Lektier = "Beskriv ét eksempel på hver af Newtons 3 love fra dit hverdagsliv." }
        @{ Beskrivelse = "Astronomi: solsystemet og planeter. Vi bruger stjernekort."; Lektier = "Find planeten Jupiter på himlen i dag og beskriv dens position." }
    )
    "Samfundsfag"         = @(
        @{ Beskrivelse = "Demokrati: hvordan fungerer Folketing og regering?"; Lektier = "Læs om tre politiske partier og skriv hvad de mener om skatter." }
        @{ Beskrivelse = "Medier og fake news: kildekritik på nettet."; Lektier = "Find en artikel du tror kan være fake news og analyser den." }
        @{ Beskrivelse = "Økonomi: hvad er BNP og velfærdsstat?"; Lektier = "Forklar velfærdsstaten for en forælder derhjemme." }
        @{ Beskrivelse = "Globalisering: hvad betyder det at verden er forbundet?"; Lektier = "Kig på mærket i 5 stykker tøj og notér produktionslandet." }
        @{ Beskrivelse = "EU: hvad er EU og hvad bestemmer de?"; Lektier = "Find ét EU-direktiv der påvirker din hverdag og beskriv det." }
    )
}

# ─── step 0: authenticate ──────────────────────────────────────────────────────

Write-Host ""
Write-Host "[0/11] Authenticating with Keycloak..." -ForegroundColor Cyan
$token = Get-Token
Write-Host "       Token acquired." -ForegroundColor Green

# ─── step 1: time slot template ────────────────────────────────────────────────

Write-Host ""
Write-Host "[1/11] Creating time slot template (08:00–14:45, 45 min, 2 breaks)..." -ForegroundColor Cyan

$templateBody = @{
    lessonDurationMinutes = 45
    dayStartTime          = "08:00:00"
    dayEndTime            = "14:45:00"
    activeDays            = "Monday,Tuesday,Wednesday,Thursday,Friday"
    breaks                = @(
        @{ startTime = "10:15:00"; durationMinutes = 15 }
        @{ startTime = "12:00:00"; durationMinutes = 30 }
    )
}
Invoke-Api -Method PUT -Path "/api/v1/time-slot-template" -Body $templateBody
Write-Host "       Template created." -ForegroundColor Green

# Fetch school-level time slots so we can reference their IDs for schema slots
$schoolTimeSlots = Invoke-Api -Method GET -Path "/api/v1/time-slots"
$lessonSlots = $schoolTimeSlots | Where-Object { $_.isBreak -eq $false }
Write-Host "       Got $($lessonSlots.Count) lesson slots." -ForegroundColor Green

# ─── step 2: rooms ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[2/11] Creating rooms..." -ForegroundColor Cyan

$roomDefs = @(
    @{ name = "Biblioteket"; capacity = 40; description = "Skolens bibliotek med bogsamling og arbejdspladser" }
    @{ name = "Gymnastiksalen"; capacity = 100; description = "Stor idrætssal med springgrav og bander" }
    @{ name = "Musikrummet"; capacity = 30; description = "Lydtæt rum med xylofoner, trommer og klaviatur" }
    @{ name = "Naturfagslokalet"; capacity = 28; description = "Laboratorium med mikroskoper og kemikalier" }
    @{ name = "Billedkunstrummet"; capacity = 28; description = "Atelierlokale med vaske, staffelier og materialer" }
    @{ name = "Lokale 12"; capacity = 28; description = $null }
    @{ name = "Lokale 14"; capacity = 28; description = $null }
    @{ name = "SFO-lokalet"; capacity = 50; description = "SFO's primære opholdsrum" }
)

$rooms = @{}
foreach ($r in $roomDefs) {
    $created = Invoke-Api -Method POST -Path "/api/v1/rooms" -Body $r
    $rooms[$r.name] = $created.id
    Write-Host "       Room: $($r.name)" -ForegroundColor DarkGreen
}

# ─── step 3: classes ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[3/11] Creating classes (0.a–8.a)..." -ForegroundColor Cyan

$classDefs = @(
    @{ name = "Børnehaveklassen"; gradeLevel = 0; description = "Skolens børnehaveklasse" }
    @{ name = "1.a"; gradeLevel = 1; description = $null }
    @{ name = "2.a"; gradeLevel = 2; description = $null }
    @{ name = "3.a"; gradeLevel = 3; description = $null }
    @{ name = "4.a"; gradeLevel = 4; description = $null }
    @{ name = "5.a"; gradeLevel = 5; description = $null }
    @{ name = "6.a"; gradeLevel = 6; description = $null }
    @{ name = "7.a"; gradeLevel = 7; description = $null }
    @{ name = "8.a"; gradeLevel = 8; description = $null }
)

$classes = @()
foreach ($c in $classDefs) {
    $body = @{ name = $c.name; gradeLevel = $c.gradeLevel }
    if ($c.description) { $body.description = $c.description }
    $created = Invoke-Api -Method POST -Path "/api/v1/classes" -Body $body
    $classes += [PSCustomObject]@{ Id = $created.id; Name = $c.name; GradeLevel = $c.gradeLevel }
    Write-Host "       Class: $($c.name)" -ForegroundColor DarkGreen
}

# ─── step 4: staff ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[4/11] Creating staff (teachers, aides, substitutes)..." -ForegroundColor Cyan

$staffDefs = @(
    @{ name = "Marianne Kjeldsen"; email = "marianne.kjeldsen@debugskolen.dk"; phone = "+4520112233"; role = "Teacher"; isAdmin = $true }
    @{ name = "Jørgen Bak"; email = "jorgen.bak@debugskolen.dk"; phone = "+4520223344"; role = "Teacher"; isAdmin = $true }
    @{ name = "Lise Nørregaard"; email = "lise.norregaard@debugskolen.dk"; phone = "+4520334455"; role = "Teacher"; isAdmin = $false }
    @{ name = "Bent Holm"; email = "bent.holm@debugskolen.dk"; phone = "+4520445566"; role = "Teacher"; isAdmin = $false }
    @{ name = "Katrine Voss"; email = "katrine.voss@debugskolen.dk"; phone = "+4520556677"; role = "Teacher"; isAdmin = $false }
    @{ name = "Henrik Frandsen"; email = "henrik.frandsen@debugskolen.dk"; phone = "+4520667788"; role = "Teacher"; isAdmin = $false }
    @{ name = "Susanne Dalgaard"; email = "susanne.dalgaard@debugskolen.dk"; phone = "+4520778899"; role = "Teacher"; isAdmin = $false }
    @{ name = "Morten Elgaard"; email = "morten.elgaard@debugskolen.dk"; phone = "+4520889900"; role = "Teacher"; isAdmin = $false }
    @{ name = "Trine Østergaard"; email = "trine.ostergaard@debugskolen.dk"; phone = "+4520990011"; role = "Teacher"; isAdmin = $false }
    @{ name = "Claus Vestergaard"; email = "claus.vestergaard@debugskolen.dk"; phone = "+4521001122"; role = "Teacher"; isAdmin = $false }
    @{ name = "Gitte Paaske"; email = "gitte.paaske@debugskolen.dk"; phone = "+4521112233"; role = "Aide"; isAdmin = $false }
    @{ name = "Rune Svensson"; email = "rune.svensson@debugskolen.dk"; phone = "+4521223344"; role = "Aide"; isAdmin = $false }
    @{ name = "Vibeke Stub"; email = "vibeke.stub@debugskolen.dk"; phone = "+4521334455"; role = "Substitute"; isAdmin = $false }
    @{ name = "Allan Drost"; email = "allan.drost@debugskolen.dk"; phone = "+4521445566"; role = "Substitute"; isAdmin = $false }
)

$staffList = @()
foreach ($s in $staffDefs) {
    $body = @{ name = $s.name; email = $s.email; phone = $s.phone; role = $s.role; isAdmin = $s.isAdmin }
    $created = Invoke-Api -Method POST -Path "/api/v1/staff" -Body $body
    $staffList += [PSCustomObject]@{ Id = $created.id; Name = $s.name; Role = $s.role }
    Write-Host "       Staff: $($s.name) ($($s.role))" -ForegroundColor DarkGreen
}

$teachers = $staffList | Where-Object { $_.Role -eq "Teacher" }
$aides = $staffList | Where-Object { $_.Role -eq "Aide" }

# ─── step 5: students ──────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[5/11] Creating students (10 per class)..." -ForegroundColor Cyan

# Distinct last names per class — each "family" has 1-2 kids in the school
$familyNames = @("Christoffersen", "Bundgaard", "Skaarup", "Elkjær", "Thygesen", "Haarby", "Søndergaard", "Kaas", "Friis", "Holst", "Funch", "Agger", "Brøndum", "Vestby", "Lykkegaard", "Damgaard", "Sandberg", "Roed", "Yde", "Lindegaard", "Brinch", "Staun", "Nørby", "Hvid", "Skov", "Gade", "Kvist", "Wulff", "Blom", "Aagaard")

$classStudents = @{}   # classId → list of studentIds
$familyNameIdx = 0

foreach ($cls in $classes) {
    $studentIds = @()
    for ($i = 0; $i -lt 10; $i++) {
        $lastName = $familyNames[$familyNameIdx % $familyNames.Count]
        $familyNameIdx++
        $firstName = $allFirstNames[($nameIndex + $i) % $allFirstNames.Count]
        $script:nameIndex++
        $body = @{ name = "$firstName $lastName"; classId = $cls.Id }
        $created = Invoke-Api -Method POST -Path "/api/v1/students" -Body $body
        $studentIds += $created.id
    }
    $classStudents[$cls.Id] = $studentIds
    Write-Host "       $($cls.Name): $($studentIds.Count) students" -ForegroundColor DarkGreen
}

# ─── step 6: parents ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "[6/11] Creating and linking parents..." -ForegroundColor Cyan

$parentLastNames = @("Munk", "Storm", "Lund", "Dahl", "Kjær", "Holt", "Borg", "Grøn", "Bro", "Fugl", "Slot", "Rand", "Bech", "Stig", "Nors", "Birk", "Feld", "Gal", "Kold", "Noe")
$parentNameIdx = 0

foreach ($cls in $classes) {
    $studentIds = $classStudents[$cls.Id]
    foreach ($studentId in $studentIds) {
        # Each student gets 1-2 parents (alternate based on index)
        $numParents = if ($parentNameIdx % 3 -eq 0) { 2 } else { 1 }
        for ($p = 0; $p -lt $numParents; $p++) {
            $pFirst = $allFirstNames[($parentNameIdx * 3 + $p * 7) % $allFirstNames.Count]
            $pLast = $parentLastNames[$parentNameIdx % $parentLastNames.Count]
            # Include parentNameIdx + p to guarantee globally unique emails
            $emailSafe = "$($pFirst.ToLower() -replace '[^a-z0-9]','').$($pLast.ToLower() -replace '[^a-z0-9]','').$($parentNameIdx)p$p"
            $pEmail = "$emailSafe@eksempel.dk"
            $body = @{
                name       = "$pFirst $pLast"
                email      = $pEmail
                studentIds = @($studentId)
            }
            try {
                Invoke-Api -Method POST -Path "/api/v1/parents/invite" -Body $body | Out-Null
            }
            catch {
                # Non-fatal — email may clash or Keycloak unreachable
            }
        }
        $parentNameIdx++
    }
    Write-Host "       $($cls.Name): parents invited" -ForegroundColor DarkGreen
}

# ─── step 7: schemas with slots ────────────────────────────────────────────────

Write-Host ""
Write-Host "[7/11] Creating schemas and filling slots (Mon-Fri)..." -ForegroundColor Cyan

# Fetch all courses so we can match by name
$allCourses = Invoke-Api -Method GET -Path "/api/v1/courses"
$courseByName = @{}
foreach ($c in $allCourses) { $courseByName[$c.name] = $c.id }

$weekdays = @("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")

# Map room names to IDs for subject→room pairing
$subjectRooms = @{
    "Musik"              = $rooms["Musikrummet"]
    "Idræt"              = $rooms["Gymnastiksalen"]
    "Naturfag"           = $rooms["Naturfagslokalet"]
    "Billedkunst"        = $rooms["Billedkunstrummet"]
    "Håndværk og design" = $rooms["Billedkunstrummet"]
    "Fysik/kemi"         = $rooms["Naturfagslokalet"]
    "Biologi"            = $rooms["Naturfagslokalet"]
}

$classSchemas = @{}   # classId → schemaId

$teacherIdx = 0
foreach ($cls in $classes) {
    # Create schema
    $schema = Invoke-Api -Method POST -Path "/api/v1/classes/$($cls.Id)/schemas" -Body @{ name = "Skema 2025/2026" }
    $schemaId = $schema.id
    $classSchemas[$cls.Id] = $schemaId

    Invoke-Api -Method PUT -Path "/api/v1/classes/$($cls.Id)/schemas/$schemaId/daterange" -Body @{
        startDate = "2025-08-01"
        endDate   = "2026-06-30"
    } | Out-Null

    $courses = $gradeCourses[[int]$cls.GradeLevel]
    if (-not $courses) { $courses = $gradeCourses[5] }

    # Assign one primary teacher per class (rotate through teachers)
    $primaryTeacher = $teachers[$teacherIdx % $teachers.Count]
    $teacherIdx++

    # Fill Mon-Fri × lesson slots
    $courseIdx = 0
    foreach ($day in $weekdays) {
        foreach ($slot in $lessonSlots) {
            $courseName = $courses[$courseIdx % $courses.Count]
            $courseIdx++
            $courseId = $courseByName[$courseName]
            if (-not $courseId) { continue }

            # Use specialized teacher for some subjects, primary teacher otherwise
            $teacherId = $primaryTeacher.Id

            $roomId = $subjectRooms[$courseName]

            $slotBody = @{
                timeSlotId = $slot.id
                weekday    = $day
                courseId   = $courseId
                teacherId  = $teacherId
            }
            if ($roomId) { $slotBody.roomId = $roomId }

            try {
                Invoke-Api -Method PUT -Path "/api/v1/classes/$($cls.Id)/schemas/$schemaId/slots" -Body $slotBody | Out-Null
            }
            catch {
                # Conflict or duplicate — skip silently
            }
        }
    }
    Write-Host "       $($cls.Name): schema filled" -ForegroundColor DarkGreen
}

# ─── step 8: week plan notes ───────────────────────────────────────────────────

Write-Host ""
Write-Host "[8/11] Adding week plan notes (weeks 2-5, ISO 2026)..." -ForegroundColor Cyan

$templateIdx = @{}   # courseName → rotating index

foreach ($cls in $classes) {
    $schemaId = $classSchemas[$cls.Id]
    # Fetch schema detail to get slot IDs
    $detail = Invoke-Api -Method GET -Path "/api/v1/classes/$($cls.Id)/schemas/$schemaId"
    $schemaSlots = $detail.slots

    foreach ($isoWeek in 2..5) {
        foreach ($schemaSlot in $schemaSlots) {
            $courseName = $schemaSlot.courseName
            $templates = $weekPlanTemplates[$courseName]
            if (-not $templates) { continue }

            if (-not $templateIdx[$courseName]) { $templateIdx[$courseName] = 0 }
            $t = $templates[$templateIdx[$courseName] % $templates.Count]
            $templateIdx[$courseName]++

            $body = @{
                schemaSlotId    = $schemaSlot.id
                description     = $t.Beskrivelse
                lektier         = $t.Lektier
                fagSwapCourseId = $null
            }
            $queryString = "?isoYear=2026&isoWeek=$isoWeek&schemaId=$schemaId"
            try {
                Invoke-Api -Method PUT -Path "/api/v1/classes/$($cls.Id)/week-plan/slots$queryString" -Body $body | Out-Null
            }
            catch {
                # Non-fatal
            }
        }
    }
    Write-Host "       $($cls.Name): week plan notes added" -ForegroundColor DarkGreen
}

# ─── step 9: calendar entries ──────────────────────────────────────────────────

Write-Host ""
Write-Host "[9/12] Creating calendar entries..." -ForegroundColor Cyan

# Fetch standard Danish school holidays for 2025 (= school year 2025/2026)
$defaults = Invoke-Api -Method GET -Path "/api/v1/calendar/defaults?year=2025"
foreach ($entry in $defaults) {
    $body = @{ title = $entry.title; type = $entry.type; startDate = $entry.startDate; endDate = $entry.endDate }
    Invoke-Api -Method POST -Path "/api/v1/calendar" -Body $body | Out-Null
    Write-Host "       Calendar: $($entry.title)" -ForegroundColor DarkGreen
}

# Extra school-specific entries
$extraCalendarEntries = @(
    @{ title = "Planlægningsdag"; type = "Arbejdsdag"; startDate = "2026-01-02"; endDate = "2026-01-02" }
    @{ title = "Pædagogisk dag"; type = "Arbejdsdag"; startDate = "2026-03-02"; endDate = "2026-03-02" }
    @{ title = "Pædagogisk dag"; type = "Arbejdsdag"; startDate = "2026-08-10"; endDate = "2026-08-10" }
    @{ title = "Skolefest"; type = "Begivenhed"; startDate = "2026-03-20"; endDate = "2026-03-20" }
    @{ title = "Trivselsdag"; type = "Begivenhed"; startDate = "2026-04-28"; endDate = "2026-04-28" }
    @{ title = "Forældremøde"; type = "Begivenhed"; startDate = "2026-09-03"; endDate = "2026-09-03"; recurrenceRule = "FREQ=YEARLY" }
    @{ title = "Motionsdag"; type = "Begivenhed"; startDate = "2026-09-25"; endDate = "2026-09-25" }
)
foreach ($entry in $extraCalendarEntries) {
    $body = @{ title = $entry.title; type = $entry.type; startDate = $entry.startDate; endDate = $entry.endDate }
    if ($entry.ContainsKey('recurrenceRule')) { $body.recurrenceRule = $entry.recurrenceRule }
    Invoke-Api -Method POST -Path "/api/v1/calendar" -Body $body | Out-Null
    Write-Host "       Calendar: $($entry.title)" -ForegroundColor DarkGreen
}

# ─── step 10: vacation registration windows ────────────────────────────────────

Write-Host ""
Write-Host "[10/12] Creating vacation registration windows..." -ForegroundColor Cyan

$vacationWindows = @(
    @{
        title                = "Sommerferie 2026 — SFO ferietilmelding"
        registrationDeadline = "2026-05-31"
        careStartDate        = "2026-06-27"
        careEndDate          = "2026-08-09"
        granularity          = "Weeks"
        isOpen               = $true
    }
    @{
        title                = "Vinterferie 2026 — SFO ferietilmelding"
        registrationDeadline = "2026-02-01"
        careStartDate        = "2026-02-14"
        careEndDate          = "2026-02-22"
        granularity          = "Days"
        isOpen               = $false
    }
    @{
        title                = "Påskeferie 2026 — SFO ferietilmelding"
        registrationDeadline = "2026-03-22"
        careStartDate        = "2026-04-02"
        careEndDate          = "2026-04-13"
        granularity          = "Days"
        isOpen               = $false
    }
)

foreach ($vw in $vacationWindows) {
    try {
        Invoke-Api -Method POST -Path "/api/v1/vacation-registration" -Body $vw | Out-Null
        Write-Host "       Vacation window: $($vw.title)" -ForegroundColor DarkGreen
    }
    catch {
        Write-Warning "       Skipping vacation window '$($vw.title)'"
    }
}

# ─── step 11: SFO shifts and week plans ────────────────────────────────────────

Write-Host ""
Write-Host "[11/12] Creating SFO shifts and week plan notes..." -ForegroundColor Cyan

$sfoShiftDefs = @(
    @{ dayOfWeek = 1; startTime = "06:30"; endTime = "08:00"; label = "Morgen SFO — mandag" }
    @{ dayOfWeek = 2; startTime = "06:30"; endTime = "08:00"; label = "Morgen SFO — tirsdag" }
    @{ dayOfWeek = 3; startTime = "06:30"; endTime = "08:00"; label = "Morgen SFO — onsdag" }
    @{ dayOfWeek = 4; startTime = "06:30"; endTime = "08:00"; label = "Morgen SFO — torsdag" }
    @{ dayOfWeek = 5; startTime = "06:30"; endTime = "08:00"; label = "Morgen SFO — fredag" }
    @{ dayOfWeek = 1; startTime = "14:00"; endTime = "17:00"; label = "Eftermiddag SFO — mandag" }
    @{ dayOfWeek = 2; startTime = "14:00"; endTime = "17:00"; label = "Eftermiddag SFO — tirsdag" }
    @{ dayOfWeek = 3; startTime = "14:00"; endTime = "16:00"; label = "Eftermiddag SFO — onsdag (tidlig)" }
    @{ dayOfWeek = 4; startTime = "14:00"; endTime = "17:00"; label = "Eftermiddag SFO — torsdag" }
    @{ dayOfWeek = 5; startTime = "14:00"; endTime = "17:00"; label = "Eftermiddag SFO — fredag" }
)

$sfoShiftIds = @()
$aideIdx = 0
foreach ($s in $sfoShiftDefs) {
    $body = @{ dayOfWeek = $s.dayOfWeek; startTime = $s.startTime; endTime = $s.endTime; label = $s.label }
    $created = Invoke-Api -Method POST -Path "/api/v1/sfo/shifts" -Body $body
    $sfoShiftIds += $created.id

    # Assign one of the aides to the shift
    if ($aides.Count -gt 0) {
        $aide = $aides[$aideIdx % $aides.Count]
        $aideIdx++
        try {
            Invoke-Api -Method POST -Path "/api/v1/sfo/shifts/$($created.id)/staff/$($aide.Id)" | Out-Null
        }
        catch { }
    }
    Write-Host "       SFO shift: $($s.label)" -ForegroundColor DarkGreen
}

$sfoBeskrivelser = @(
    "Leg og aktiviteter i SFO-lokalet. Vi bygger med LEGO og spiller brætspil."
    "Udedag! Eleverne er i skolegården og nærliggende park."
    "Kreativ workshop: vi laver fugle af papmaché."
    "Film-eftermiddag: vi ser en kort film og laver popcorn."
    "Boldspil og løbespil på boldbanen."
    "Madlavning: vi bager boller med rosiner."
    "Naturvandring langs stien bag skolen."
    "Tegne- og malerworkshop med vandfarver."
)

$beskIdx = 0
foreach ($isoWeek in 2..5) {
    foreach ($shiftId in $sfoShiftIds) {
        $body = @{
            isoYear     = 2026
            isoWeek     = $isoWeek
            sfoShiftId  = $shiftId
            description = $sfoBeskrivelser[$beskIdx % $sfoBeskrivelser.Count]
        }
        $beskIdx++
        try {
            Invoke-Api -Method PUT -Path "/api/v1/sfo/week-plan/shifts" -Body $body | Out-Null
        }
        catch { }
    }
}
Write-Host "       SFO week plans: done" -ForegroundColor DarkGreen

# ─── step 11: file folders and fake files ──────────────────────────────────────

Write-Host ""
Write-Host "[12/12] Creating file folders and uploading sample files..." -ForegroundColor Cyan

$folderDefs = @(
    @{ name = "Dansk materialer"; parentId = $null; courseId = $null }
    @{ name = "Matematik"; parentId = $null; courseId = $null }
    @{ name = "Fælles ressourcer"; parentId = $null; courseId = $null }
    @{ name = "Årsplaner"; parentId = $null; courseId = $null }
)

$folderIds = @{}
foreach ($f in $folderDefs) {
    $body = @{ name = $f.name }
    $created = Invoke-Api -Method POST -Path "/api/v1/files/folders" -Body $body
    $folderIds[$f.name] = $created.id
    Write-Host "       Folder: $($f.name)" -ForegroundColor DarkGreen
}

# Upload tiny text files (real presign+PUT+confirm flow)
$fileDefs = @(
    @{ fileName = "Årsplan-Dansk-2025-26.txt"; folder = "Dansk materialer"; content = "Årsplan for Dansk 2025/2026.`nForår: Novellen`nEfterår: Lyrik og drama" }
    @{ fileName = "Årsplan-Matematik-2025-26.txt"; folder = "Matematik"; content = "Årsplan for Matematik 2025/2026.`nForår: Geometri og statistik`nEfterår: Algebra" }
    @{ fileName = "Forretningsorden-elevraadet.txt"; folder = "Fælles ressourcer"; content = "Forretningsorden for elevrådet ved Debugskolen.`n§1 Elevrådet mødes én gang om måneden." }
    @{ fileName = "Trivselspolitik-2025.txt"; folder = "Fælles ressourcer"; content = "Trivselspolitik 2025.`nDebugskolen arbejder aktivt med trivsel og inklusion." }
    @{ fileName = "Årshjul-2025-26.txt"; folder = "Årsplaner"; content = "Årshjul for Debugskolen 2025/2026.`nAugust: Skolestart`nDecember: Julefest`nMarts: Skolefest" }
    @{ fileName = "Laerervejledning-laesning.txt"; folder = "Dansk materialer"; content = "Lærervejledning: Tidlig læsning og læsestrategier for indskolingen." }
)

foreach ($fd in $fileDefs) {
    $folderId = $folderIds[$fd.folder]
    $contentBytes = [System.Text.Encoding]::UTF8.GetBytes($fd.content)
    $fileSize = $contentBytes.Length

    # 1. Presign
    $presignBody = @{ fileName = $fd.fileName; fileSizeBytes = $fileSize; folderId = $folderId }
    try {
        $presign = Invoke-Api -Method POST -Path "/api/v1/files/presign" -Body $presignBody
    }
    catch {
        Write-Warning "       Skipping file '$($fd.fileName)' (presign failed)"
        continue
    }

    # 2. PUT to S3/LocalStack
    try {
        Invoke-RestMethod `
            -Uri $presign.uploadUrl `
            -Method PUT `
            -Body $contentBytes `
            -ContentType $presign.contentType `
            -Headers @{ "Content-Length" = $fileSize } | Out-Null
    }
    catch {
        Write-Warning "       S3 PUT failed for '$($fd.fileName)' — skipping confirm"
        continue
    }

    # 3. Confirm
    try {
        Invoke-Api -Method POST -Path "/api/v1/files/confirm" -Body @{ confirmToken = $presign.confirmToken } | Out-Null
        Write-Host "       File: $($fd.fileName)" -ForegroundColor DarkGreen
    }
    catch {
        Write-Warning "       Confirm failed for '$($fd.fileName)'"
    }
}

# ─── done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host " Seeding complete for Debugskolen!" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host " Rooms:         $($roomDefs.Count)"
Write-Host " Classes:       $($classes.Count)"
Write-Host " Staff:         $($staffDefs.Count) (+ 1 seed admin)"
Write-Host " Students:      ~$($classes.Count * 10)"
Write-Host " Schema slots:  Mon-Fri x $($lessonSlots.Count) lesson slots x $($classes.Count) classes"
Write-Host " Week plans:    Weeks 2-5 / 2026, all classes"
Write-Host " Calendar:      $($defaults.Count + $extraCalendarEntries.Count) entries ($($defaults.Count) standard + $($extraCalendarEntries.Count) extra)"
Write-Host " Vacation windows: $($vacationWindows.Count) (sommerferie open, vinter+påske closed)"
Write-Host " SFO shifts:    $($sfoShiftDefs.Count) (with week plans weeks 2-5)"
Write-Host " File folders:  $($folderDefs.Count)"
Write-Host " Files:         $($fileDefs.Count)"
Write-Host ""
Write-Host " Log in at http://localhost:5173" -ForegroundColor Yellow
Write-Host "   User:     $Username" -ForegroundColor Yellow
Write-Host "   Password: $Password" -ForegroundColor Yellow
Write-Host ""
