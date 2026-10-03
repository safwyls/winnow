import './artwork-placeholder.css'

// Local illustrations stay visible offline and inherit live palette changes, including portaled views.
export function ArtworkPlaceholder({
  label = 'Artwork unavailable',
  className = '',
}: {
  label?: string
  className?: string
}) {
  return (
    <span className={`art-placeholder artwork-placeholder ${className}`}>
      <svg
        className="artwork-placeholder-image"
        viewBox="0 0 600 600"
        preserveAspectRatio="xMidYMid slice"
        aria-hidden="true"
        focusable="false"
      >
        <g data-placeholder-scene="avalon">
          <circle className="placeholder-light" cx="400" cy="214" r="114" opacity=".06" />
          <circle className="placeholder-light" cx="400" cy="214" r="76" opacity=".09" />
          <circle className="placeholder-light" cx="400" cy="214" r="42" opacity=".22" />
          <path
            className="placeholder-distant"
            d="M0 376 74 293 112 332 224 226 291 311 347 266 469 359 545 306 600 352V600H0Z"
          />
          <path className="placeholder-middle" d="M0 389Q102 318 194 379T362 376T600 390V600H0Z" />
          <path className="placeholder-near" d="M0 455Q94 370 205 433T399 430T600 449V600H0Z" />
          <path
            className="placeholder-mist"
            d="M-20 393Q90 365 210 397T425 397T620 390M-20 420Q110 391 253 423T470 420T620 423"
          />
        </g>
        <g data-placeholder-scene="afterglow">
          <circle className="placeholder-light" cx="308" cy="292" r="234" opacity=".035" />
          <circle className="placeholder-light" cx="308" cy="292" r="177" opacity=".045" />
          <circle className="placeholder-light" cx="308" cy="292" r="124" opacity=".06" />
          <circle className="placeholder-light" cx="308" cy="292" r="74" opacity=".11" />
          <circle className="placeholder-light" cx="308" cy="292" r="36" opacity=".28" />
          <path
            className="placeholder-halo"
            d="M123 329A190 190 0 0 1 468 190M166 422A164 164 0 0 0 460 336M212 212A114 114 0 0 1 420 313"
          />
          <path
            className="placeholder-mist"
            d="M0 442Q179 421 339 443T600 440M0 460Q232 445 380 463T600 460"
          />
        </g>
        <g data-placeholder-scene="rift">
          <ellipse
            className="placeholder-light"
            cx="328"
            cy="285"
            rx="238"
            ry="83"
            transform="rotate(-32 328 285)"
            opacity=".045"
          />
          <ellipse
            className="placeholder-light"
            cx="328"
            cy="285"
            rx="187"
            ry="46"
            transform="rotate(-32 328 285)"
            opacity=".045"
          />
          <g className="placeholder-stars">
            {Array.from({ length: 38 }, (_, index) => (
              <circle
                key={index}
                cx={18 + ((index * 137 + 39) % 566)}
                cy={24 + ((index * 193 + 71) % 550)}
                r={index % 7 === 0 ? 1.8 : 1}
                opacity={0.25 + (index % 4) * 0.17}
              />
            ))}
            <path
              d="M405 208v14m-7-7h14M176 365v10m-5-5h10"
              fill="none"
              stroke="currentColor"
              strokeWidth="1"
              opacity=".8"
            />
          </g>
          <circle className="placeholder-orbit" cx="317" cy="311" r="96" />
          <circle className="placeholder-light" cx="245" cy="248" r="4" opacity=".65" />
        </g>
        <g data-placeholder-scene="catalogue">
          <path className="placeholder-paper" d="M162 183 421 204 400 415 141 394Z" />
          <path className="placeholder-paper" d="M182 161 441 182 420 393 161 372Z" />
          <path className="placeholder-paper-front" d="M172 185H432V397H172Z" />
          <path
            className="placeholder-print"
            d="M198 221H252M198 235H283M198 276H406M198 296H406M198 316H406M198 336H361M198 357H324M380 211V242M386 211V242M396 211V242M402 211V242"
          />
          <path className="placeholder-print" d="M172 255H432M220 255V397" opacity=".35" />
          <path
            className="placeholder-print"
            d="M142 153V139H156M448 153V139H434M142 427V441H156M448 427V441H434"
            opacity=".45"
          />
        </g>
      </svg>
      {label && <span className="artwork-placeholder-label">{label}</span>}
    </span>
  )
}
