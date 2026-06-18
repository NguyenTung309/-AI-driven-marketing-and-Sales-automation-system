import "./App.css";

function App() {
  return (
    <>
      <section id="center">
        <div className="brand-mark" aria-hidden="true">
          HB
        </div>
        <div>
          <h1>Học Bá AI</h1>
          <p>Trung tâm vận hành tư vấn, nội dung và chăm sóc học viên đã sẵn sàng.</p>
        </div>
      </section>

      <div className="ticks" />

      <section id="next-steps">
        <div id="docs">
          <svg className="icon" role="presentation" aria-hidden="true">
            <use href="/icons.svg#documentation-icon" />
          </svg>
          <h2>Vận hành tập trung</h2>
          <p>Theo dõi hội thoại, nội dung, tài liệu và các agent trong một nơi.</p>
        </div>
        <div id="social">
          <svg className="icon" role="presentation" aria-hidden="true">
            <use href="/icons.svg#social-icon" />
          </svg>
          <h2>Hỗ trợ đội ngũ</h2>
          <p>Ưu tiên thao tác rõ ràng, ít thuật ngữ kỹ thuật và dễ bàn giao.</p>
        </div>
      </section>

      <div className="ticks" />
      <section id="spacer" />
    </>
  );
}

export default App;
