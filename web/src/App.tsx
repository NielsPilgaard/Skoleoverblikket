import './App.css'
import { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { HelmetProvider } from 'react-helmet-async'
import ScrollToTop from './components/ScrollToTop'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthProvider'
import { useAuth } from './auth/useAuth'
import Layout from './components/Layout'
import ViewModeToolbar from './components/ViewModeToolbar'

// Keep critical public pages as regular imports
import LandingPage from './pages/LandingPage'
import LoginPage from './pages/LoginPage'
import SignupPage from './pages/SignupPage'
import InvitationAcceptPage from './pages/InvitationAcceptPage'
import OmPage from './pages/OmPage'
import KontaktPage from './pages/KontaktPage'

// Lazy load legal/info pages
const PrivatlivspolitikPage = lazy(() => import('./pages/PrivatlivspolitikPage'))

// Lazy load all other pages
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const MySchedulePage = lazy(() => import('./pages/MySchedulePage'))
const StaffDashboardPage = lazy(() => import('./pages/StaffDashboardPage'))
const ClassesPage = lazy(() => import('./pages/ClassesPage'))
const SchemaBuilderPage = lazy(() => import('./pages/SchemaBuilderPage'))
const StaffPage = lazy(() => import('./pages/StaffPage'))
const StaffSchedulePage = lazy(() => import('./pages/StaffSchedulePage'))
const CoursesPage = lazy(() => import('./pages/CoursesPage'))
const RoomsPage = lazy(() => import('./pages/RoomsPage'))
const RoomSchedulePage = lazy(() => import('./pages/RoomSchedulePage'))
const PrintSchemaPage = lazy(() => import('./pages/PrintSchemaPage'))
const SfoPrintPage = lazy(() => import('./pages/SfoPrintPage'))
const UgeplanPrintPage = lazy(() => import('./pages/UgeplanPrintPage'))
const SkoleindstillingerPage = lazy(() => import('./pages/SkoleindstillingerPage'))
const SchoolSetupWizardPage = lazy(() => import('./pages/SchoolSetupWizardPage'))
const FilesPage = lazy(() => import('./pages/FilesPage'))
const BillingPage = lazy(() => import('./pages/BillingPage'))
const ExportsPage = lazy(() => import('./pages/ExportsPage'))
const ClassTimeSlotsPage = lazy(() => import('./pages/ClassTimeSlotsPage'))
const CalendarPage = lazy(() => import('./pages/CalendarPage'))
const WeekPlanPage = lazy(() => import('./pages/WeekPlanPage'))
const SfoPage = lazy(() => import('./pages/SfoPage'))
const StudentsPage = lazy(() => import('./pages/StudentsPage'))
const ParentsPage = lazy(() => import('./pages/ParentsPage'))
const YearRollPage = lazy(() => import('./pages/AarsrulPage'))
const ParentSchemaPage = lazy(() => import('./pages/parent/ParentSchemaPage'))
const ParentCalendarPage = lazy(() => import('./pages/parent/ParentCalendarPage'))
const ParentUgeplanPage = lazy(() => import('./pages/parent/ParentUgeplanPage'))
const ParentProfilePage = lazy(() => import('./pages/parent/ParentProfilePage'))
const BackofficeLayout = lazy(() => import('./pages/backoffice/BackofficeLayout'))
const BackofficeTenantsPage = lazy(() => import('./pages/backoffice/BackofficeTenantsPage'))
const BackofficeTenantDetailPage = lazy(
  () => import('./pages/backoffice/BackofficeTenantDetailPage')
)
const BackofficeEmailPreviewPage = lazy(
  () => import('./pages/backoffice/BackofficeEmailPreviewPage')
)
const ParentDirectoryPage = lazy(() => import('./pages/ParentDirectoryPage'))
const ParentFravaerPage = lazy(() => import('./pages/parent/ParentFravaerPage'))
const FravaerPage = lazy(() => import('./pages/FravaerPage'))
const NotificationPreferencesPage = lazy(() => import('./pages/NotificationPreferencesPage'))
const KlassechatPage = lazy(() => import('./pages/KlassechatPage'))
const KontaktbogPage = lazy(() => import('./pages/KontaktbogPage'))
const ParentKontaktbogPage = lazy(() => import('./pages/parent/ParentKontaktbogPage'))
const BeskederPage = lazy(() => import('./pages/BeskederPage'))
const FerieindmeldingPage = lazy(() => import('./pages/FerieindmeldingPage'))
const FerieindmeldingDetailPage = lazy(() => import('./pages/FerieindmeldingDetailPage'))
const ParentFerieindmeldingPage = lazy(() => import('./pages/parent/ParentFerieindmeldingPage'))
const BestyrelseDashboardPage = lazy(() => import('./pages/BestyrelseDashboardPage'))
const BestyrelseFilerPage = lazy(() => import('./pages/BestyrelseFilerPage'))
const BestyrelseSkemaerPage = lazy(() => import('./pages/BestyrelseSkemaerPage'))
const BestyrelseMedarbejderePage = lazy(() => import('./pages/BestyrelseMedarbejderePage'))
const StaaMaalMedPage = lazy(() => import('./pages/StaaMaalMedPage'))
const BoardInvitationPage = lazy(() => import('./pages/BoardInvitationPage'))
const ImportPage = lazy(() => import('./pages/ImportPage'))
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 10_000,
    },
  },
})

function HomeRedirect() {
  const { authenticated, isAdmin, isParent, isBoard, isSuperAdmin, viewAs } = useAuth()
  if (authenticated) {
    if (isSuperAdmin && viewAs === 'default') return <Navigate to="/backoffice" replace />
    if (isParent) return <Navigate to="/foraeldrevisning/skema" replace />
    if (isBoard) return <Navigate to="/bestyrelse/oversigt" replace />
    return <Navigate to={isAdmin ? '/dashboard' : '/mig/oversigt'} replace />
  }
  return <LandingPage />
}

function AdminRoute({ children }: { children: JSX.Element }) {
  const { isAdmin } = useAuth()
  if (!isAdmin) return <Navigate to="/mig/oversigt" replace />
  return <>{children}</>
}

function ParentRoute({ children }: { children: JSX.Element }) {
  const { isParent } = useAuth()
  // Route through HomeRedirect — the caller may be staff or admin, and each
  // resolves to a different landing page.
  if (!isParent) return <Navigate to="/" replace />
  return <>{children}</>
}

function SuperAdminRoute({ children }: { children: JSX.Element }) {
  const { isSuperAdmin } = useAuth()
  if (!isSuperAdmin) return <Navigate to="/dashboard" replace />
  return <>{children}</>
}

function BoardRoute({ children }: { children: JSX.Element }) {
  const { isBoard } = useAuth()
  // Route through HomeRedirect — see ParentRoute.
  if (!isBoard) return <Navigate to="/" replace />
  return <>{children}</>
}

/** Staff-only (Teacher/Aide/Vikar) — admins, parents, board members and super admins land elsewhere. */
function StaffRoute({ children }: { children: JSX.Element }) {
  const { isAdmin, isParent, isBoard, isSuperAdmin } = useAuth()
  if (isAdmin || isParent || isBoard || isSuperAdmin) return <Navigate to="/" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <HelmetProvider>
      <AuthProvider>
        <QueryClientProvider client={queryClient}>
          <BrowserRouter>
            <ScrollToTop />
            <ViewModeToolbar />
            <Suspense
              fallback={
                <div className="flex items-center justify-center min-h-screen">Indlæser...</div>
              }
            >
              <Routes>
                {/* Public routes */}
                <Route path="/" element={<HomeRedirect />} />
                <Route path="login" element={<LoginPage />} />
                <Route path="signup" element={<SignupPage />} />
                <Route path="invitation/:token" element={<InvitationAcceptPage />} />
                <Route path="parent-invitation/:token" element={<InvitationAcceptPage />} />
                <Route path="board-invitation/:token" element={<BoardInvitationPage />} />
                <Route path="om" element={<OmPage />} />
                <Route path="privatlivspolitik" element={<PrivatlivspolitikPage />} />
                <Route path="kontakt" element={<KontaktPage />} />
                <Route path="udskriv/klasse/:classId" element={<PrintSchemaPage />} />
                <Route path="udskriv/medarbejder/:staffId" element={<PrintSchemaPage />} />
                <Route path="udskriv/lokale/:roomId" element={<PrintSchemaPage />} />
                <Route path="udskriv/sfo" element={<SfoPrintPage />} />
                <Route path="udskriv/ugeplan" element={<UgeplanPrintPage />} />

                {/* Pages outside Layout (no sidebar) */}
                <Route path="setup" element={<SchoolSetupWizardPage />} />
                <Route
                  path="backoffice"
                  element={
                    <SuperAdminRoute>
                      <BackofficeLayout />
                    </SuperAdminRoute>
                  }
                >
                  <Route index element={<Navigate to="tenants" replace />} />
                  <Route path="tenants" element={<BackofficeTenantsPage />} />
                  <Route path="tenants/:schoolId" element={<BackofficeTenantDetailPage />} />
                  <Route path="emails" element={<BackofficeEmailPreviewPage />} />
                </Route>

                {/* Authenticated app */}
                <Route path="/" element={<Layout />}>
                  <Route
                    path="dashboard"
                    element={
                      <AdminRoute>
                        <DashboardPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="mig/oversigt"
                    element={
                      <StaffRoute>
                        <StaffDashboardPage />
                      </StaffRoute>
                    }
                  />
                  <Route path="mig/skema" element={<MySchedulePage />} />
                  <Route path="klasser" element={<ClassesPage />} />
                  <Route path="klasser/:classId/skema/:schemaId" element={<SchemaBuilderPage />} />
                  <Route
                    path="klasser/:classId/lektioner"
                    element={
                      <AdminRoute>
                        <ClassTimeSlotsPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="klasser/:classId/schemas/:schemaId/lektioner"
                    element={<ClassTimeSlotsPage />}
                  />
                  <Route
                    path="medarbejdere"
                    element={
                      <AdminRoute>
                        <StaffPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="medarbejdere/:staffId/skema"
                    element={
                      <AdminRoute>
                        <StaffSchedulePage />
                      </AdminRoute>
                    }
                  />
                  <Route path="fag" element={<CoursesPage />} />
                  <Route path="lokaler" element={<RoomsPage />} />
                  <Route path="lokaler/:roomId/skema" element={<RoomSchedulePage />} />
                  <Route path="filer" element={<FilesPage />} />
                  <Route
                    path="eksporter"
                    element={
                      <AdminRoute>
                        <ExportsPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="abonnement"
                    element={
                      <AdminRoute>
                        <BillingPage />
                      </AdminRoute>
                    }
                  />
                  <Route path="kalender" element={<CalendarPage />} />
                  <Route path="klasser/:classId/ugeplan" element={<WeekPlanPage />} />
                  <Route
                    path="indstillinger"
                    element={
                      <AdminRoute>
                        <SkoleindstillingerPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="sfo"
                    element={
                      <AdminRoute>
                        <SfoPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="elever"
                    element={
                      <AdminRoute>
                        <StudentsPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="foraeldre"
                    element={
                      <AdminRoute>
                        <ParentsPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="aarsrul"
                    element={
                      <AdminRoute>
                        <YearRollPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/skema"
                    element={
                      <ParentRoute>
                        <ParentSchemaPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/kalender"
                    element={
                      <ParentRoute>
                        <ParentCalendarPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/ugeplan"
                    element={
                      <ParentRoute>
                        <ParentUgeplanPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/kontakt"
                    element={
                      <ParentRoute>
                        <ParentDirectoryPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/fravaer"
                    element={
                      <ParentRoute>
                        <ParentFravaerPage />
                      </ParentRoute>
                    }
                  />
                  <Route path="fravaer" element={<FravaerPage />} />
                  <Route path="klassechat" element={<KlassechatPage />} />
                  <Route path="kontaktbog" element={<KontaktbogPage />} />
                  <Route
                    path="foraeldrevisning/kontaktbog"
                    element={
                      <ParentRoute>
                        <ParentKontaktbogPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/profil"
                    element={
                      <ParentRoute>
                        <ParentProfilePage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="indstillinger/notifikationer"
                    element={<NotificationPreferencesPage />}
                  />
                  <Route path="beskeder" element={<BeskederPage />} />
                  <Route
                    path="ferieindmelding"
                    element={
                      <AdminRoute>
                        <FerieindmeldingPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="ferieindmelding/:id"
                    element={
                      <AdminRoute>
                        <FerieindmeldingDetailPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="foraeldrevisning/ferieindmelding"
                    element={
                      <ParentRoute>
                        <ParentFerieindmeldingPage />
                      </ParentRoute>
                    }
                  />
                  <Route
                    path="bestyrelse/oversigt"
                    element={
                      <BoardRoute>
                        <BestyrelseDashboardPage />
                      </BoardRoute>
                    }
                  />
                  <Route
                    path="bestyrelse/filer"
                    element={
                      <BoardRoute>
                        <BestyrelseFilerPage />
                      </BoardRoute>
                    }
                  />
                  <Route
                    path="bestyrelse/skemaer"
                    element={
                      <BoardRoute>
                        <BestyrelseSkemaerPage />
                      </BoardRoute>
                    }
                  />
                  <Route
                    path="bestyrelse/medarbejdere"
                    element={
                      <BoardRoute>
                        <BestyrelseMedarbejderePage />
                      </BoardRoute>
                    }
                  />
                  <Route
                    path="staa-maal-med"
                    element={
                      <AdminRoute>
                        <StaaMaalMedPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="import"
                    element={
                      <AdminRoute>
                        <ImportPage />
                      </AdminRoute>
                    }
                  />
                  <Route
                    path="bestyrelse/staa-maal-med"
                    element={
                      <BoardRoute>
                        <StaaMaalMedPage />
                      </BoardRoute>
                    }
                  />
                </Route>
              </Routes>
            </Suspense>
          </BrowserRouter>
        </QueryClientProvider>
      </AuthProvider>
    </HelmetProvider>
  )
}
