import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";
import { session } from "./session";
import InvoiceDetailView from "./views/InvoiceDetailView.vue";
import InvoicesView from "./views/InvoicesView.vue";
import NotFoundView from "./views/NotFoundView.vue";
import SignInView from "./views/SignInView.vue";
import StationDetailView from "./views/StationDetailView.vue";
import StationsView from "./views/StationsView.vue";
import StatusView from "./views/StatusView.vue";
import TariffsView from "./views/TariffsView.vue";

// Paths and names are the product's routes: page discovery reads them from the live Vue Router, so a
// rename shows up in the report's page inventory.
export const routes: RouteRecordRaw[] = [
  {
    path: "/sign-in",
    name: "sign-in",
    component: SignInView,
    meta: { public: true, title: "Sign in" }
  },
  {
    path: "/",
    name: "stations",
    component: StationsView,
    meta: { title: "Charging stations" }
  },
  {
    path: "/stations/:stationId",
    name: "station",
    component: StationDetailView,
    props: true,
    meta: { title: "Station" }
  },
  {
    path: "/invoices",
    name: "invoices",
    component: InvoicesView,
    meta: { title: "Invoices" }
  },
  {
    path: "/invoices/:invoiceId",
    name: "invoice",
    component: InvoiceDetailView,
    props: true,
    meta: { title: "Invoice" }
  },
  {
    path: "/tariffs",
    name: "tariffs",
    component: TariffsView,
    meta: { title: "Tariffs" }
  },
  {
    path: "/status",
    name: "status",
    component: StatusView,
    meta: { public: true, title: "Public status" }
  },
  {
    path: "/:pathMatch(.*)*",
    name: "not-found",
    component: NotFoundView,
    meta: { public: true, title: "Not found" }
  }
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 })
});

router.beforeEach(async (to) => {
  await session.ensureInitialized();

  if (to.meta.public === true || session.state.user) {
    return true;
  }

  return {
    name: "sign-in",
    query: to.fullPath === "/" ? {} : { redirect: to.fullPath }
  };
});

router.afterEach((to) => {
  const title = (to.meta.title as string | undefined) ?? "Dashboard";
  document.title = `${title} · OpenCSMS`;
});
